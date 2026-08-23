using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Patterns;
using Flare.Ingest.Stats;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Consumer side of the batched ClickHouse insert pipeline: reads buffered
/// <see cref="LogEvent"/>s off the Redis Stream <see cref="RedisStreamLogEventSink"/>
/// writes to, accumulates a batch, and flushes it to ClickHouse once
/// <see cref="LogEventPipelineOptions.BatchSize"/> or <see cref="LogEventPipelineOptions.FlushInterval"/>
/// is reached.
/// </summary>
/// <remarks>
/// Reads via a consumer group (<c>XREADGROUP</c>) and only <c>XACK</c>s an entry after
/// it has been successfully written to ClickHouse - at-least-once delivery. A crash
/// mid-flush leaves entries un-acked in the group's pending-entries list (PEL); a
/// periodic <c>XPENDING</c>-driven scan (<see cref="ReclaimStalePendingAsync"/>) picks
/// those back up once they've aged past <see cref="LogEventPipelineOptions.ReclaimIdle"/> -
/// the same mechanism handles both a crash-before-ack and a deliberate no-ack-on-failure
/// retry, so there's only one recovery path to reason about. Entries whose delivery
/// count exceeds <see cref="LogEventPipelineOptions.MaxDeliveryAttempts"/> are logged and
/// dropped (acked without inserting) - a bounded poison-message safety net, not a full
/// dead-letter subsystem (a Later-item if it turns out to be needed).
///
/// Reclaim deliberately uses <c>XPENDING</c> (<c>StreamPendingMessagesAsync</c>) to find
/// stale entries and decide drop-vs-reprocess, rather than <c>XAUTOCLAIM</c>
/// (<c>StreamAutoClaimAsync</c>) alone: confirmed live against a real Redis instance that
/// StackExchange.Redis 2.13.1's <c>StreamAutoClaimAsync</c> always returns
/// <c>DeliveryCount = 0</c> on claimed entries (the XAUTOCLAIM reply doesn't carry
/// per-entry delivery-count metadata over the wire - XPENDING's extended form is the
/// only command that does), which would otherwise reclaim a poison entry forever without
/// ever recognizing it's exceeded <see cref="LogEventPipelineOptions.MaxDeliveryAttempts"/>.
///
/// StackExchange.Redis's <c>XREADGROUP</c> binding has no <c>BLOCK</c> parameter (and no
/// per-call <see cref="CancellationToken"/> support at all - confirmed via reflection
/// over the restored assembly), so this polls with <see cref="LogEventPipelineOptions.PollDelay"/>
/// between empty reads rather than blocking on the shared connection.
/// </remarks>
public sealed class ClickHouseFlushWorker(
    IConnectionMultiplexer connectionMultiplexer,
    IClickHouseLogEventWriter writer,
    IOptions<LogEventPipelineOptions> options,
    IFlushHealthTracker flushHealth,
    ILogPatternAnnotator patternAnnotator,
    ILogger<ClickHouseFlushWorker> logger) : BackgroundService
{
    private static readonly RedisValue DataField = "data";
    private static readonly RedisValue NewMessages = ">";
    private static readonly RedisValue StreamStart = "0-0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var db = connectionMultiplexer.GetDatabase();

        await EnsureConsumerGroupAsync(db, opts);

        var batch = new List<(RedisValue Id, LogEvent Event)>(opts.BatchSize);
        var lastFlush = DateTimeOffset.UtcNow;
        var lastReclaim = DateTimeOffset.UtcNow;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var toRead = opts.BatchSize - batch.Count;
                var entries = toRead > 0
                    ? await db.StreamReadGroupAsync(opts.StreamKey, opts.ConsumerGroup, opts.ConsumerName, NewMessages, toRead)
                    : [];

                AppendDeserializable(batch, entries);

                var now = DateTimeOffset.UtcNow;
                if (now - lastReclaim >= opts.ReclaimInterval)
                {
                    await ReclaimStalePendingAsync(db, opts, batch);
                    lastReclaim = now;
                }

                var shouldFlush = batch.Count >= opts.BatchSize
                    || (batch.Count > 0 && now - lastFlush >= opts.FlushInterval);

                if (shouldFlush)
                {
                    await FlushAsync(db, opts, batch, stoppingToken);
                    batch.Clear();
                    lastFlush = now;
                }
                else if (entries.Length == 0)
                {
                    await Task.Delay(opts.PollDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown - any un-flushed batch is still sitting un-acked in the
            // stream (never removed until XACK), so it's picked up again on next start.
        }
    }

    private static async Task EnsureConsumerGroupAsync(IDatabase db, LogEventPipelineOptions opts)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(opts.StreamKey, opts.ConsumerGroup, StreamStart, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // Group already exists from a prior run - expected on every restart after the first.
        }
    }

    private void AppendDeserializable(List<(RedisValue Id, LogEvent Event)> batch, StreamEntry[] entries)
    {
        foreach (var entry in entries)
        {
            if (TryDeserialize(entry, out var logEvent))
            {
                batch.Add((entry.Id, logEvent));
            }
            // Malformed entries are left un-acked (logged in TryDeserialize) - reclaimed
            // and eventually dropped by ReclaimStalePendingAsync once MaxDeliveryAttempts
            // is exceeded, same as any other failure.
        }
    }

    private bool TryDeserialize(StreamEntry entry, out LogEvent logEvent)
    {
        var raw = entry[DataField];
        if (raw.IsNullOrEmpty)
        {
            logger.LogWarning("Stream entry {Id} has no {Field} field; will be reclaimed and eventually dropped.", entry.Id, DataField);
            logEvent = null!;
            return false;
        }

        try
        {
            logEvent = JsonSerializer.Deserialize((string)raw!, LogEventJsonContext.Default.LogEvent)!;
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize stream entry {Id}; will be reclaimed and eventually dropped.", entry.Id);
            logEvent = null!;
            return false;
        }
    }

    /// <summary>
    /// Finds entries idle longer than <see cref="LogEventPipelineOptions.ReclaimIdle"/>
    /// via <c>XPENDING</c> (the only command that reports each entry's true delivery
    /// count - see the class remarks), acks-and-drops the ones over
    /// <see cref="LogEventPipelineOptions.MaxDeliveryAttempts"/> directly (XACK doesn't
    /// require owning the entry), and <c>XCLAIM</c>s the rest to fetch their content and
    /// fold them into <paramref name="batch"/> like a freshly read entry.
    /// </summary>
    private async Task ReclaimStalePendingAsync(IDatabase db, LogEventPipelineOptions opts, List<(RedisValue Id, LogEvent Event)> batch)
    {
        var minIdleMs = (long)opts.ReclaimIdle.TotalMilliseconds;
        var stalePending = await db.StreamPendingMessagesAsync(
            opts.StreamKey, opts.ConsumerGroup, opts.BatchSize, RedisValue.Null, minIdleTimeInMs: minIdleMs);

        if (stalePending.Length == 0)
        {
            return;
        }

        var toDrop = new List<RedisValue>();
        var toReclaim = new List<RedisValue>();
        foreach (var pending in stalePending)
        {
            (pending.DeliveryCount > opts.MaxDeliveryAttempts ? toDrop : toReclaim).Add(pending.MessageId);
        }

        if (toDrop.Count > 0)
        {
            foreach (var id in toDrop)
            {
                logger.LogError(
                    "Dropping poison stream entry {Id} after exceeding {MaxDeliveryAttempts} delivery attempts.",
                    id,
                    opts.MaxDeliveryAttempts);
            }
            await db.StreamAcknowledgeAsync(opts.StreamKey, opts.ConsumerGroup, [.. toDrop]);
        }

        if (toReclaim.Count > 0)
        {
            var claimed = await db.StreamClaimAsync(opts.StreamKey, opts.ConsumerGroup, opts.ConsumerName, minIdleMs, [.. toReclaim]);
            foreach (var entry in claimed)
            {
                if (TryDeserialize(entry, out var logEvent))
                {
                    batch.Add((entry.Id, logEvent));
                }
            }
        }
    }

    private async Task FlushAsync(IDatabase db, LogEventPipelineOptions opts, List<(RedisValue Id, LogEvent Event)> batch, CancellationToken cancellationToken)
    {
        var events = batch.Select(b => b.Event).ToArray();

        // Pattern-matching (Drain clustering, see LogPatternAnnotator's remarks for why
        // it runs here rather than on the OTLP receive path) happens right before the
        // ClickHouse write, on the batch as a whole. No longer purely CPU-bound/in-process
        // once LogPatternOptions.SharedStore is on (RedisPatternClusterStore does real
        // I/O) - still no dedicated try/catch here, though; any exception surfaces through
        // the same catch block that already handles a failed write.
        var annotated = await patternAnnotator.AnnotateAsync(events, cancellationToken);

        try
        {
            await writer.WriteBatchAsync(annotated);
            var ids = batch.Select(b => b.Id).ToArray();
            await db.StreamAcknowledgeAsync(opts.StreamKey, opts.ConsumerGroup, ids);
            logger.LogDebug("Flushed {Count} log events to ClickHouse.", events.Length);
            await flushHealth.RecordSuccessAsync(IngestionSignal.Logs, events.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "ClickHouse flush failed for {Count} entries; leaving un-acked for retry via PEL reclaim.",
                batch.Count);
            // Deliberately do not XACK - entries stay in the PEL and are retried once
            // they age past ReclaimIdle (see ReclaimStalePendingAsync).
            await flushHealth.RecordFailureAsync(IngestionSignal.Logs, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
