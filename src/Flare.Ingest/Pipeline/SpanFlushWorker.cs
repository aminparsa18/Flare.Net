using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Stats;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Consumer side of the batched ClickHouse insert pipeline for spans: reads buffered
/// <see cref="SpanRecord"/>s off the Redis Stream <see cref="Sinks.RedisStreamSpanEventSink"/>
/// writes to, accumulates a batch, and flushes it to ClickHouse once
/// <see cref="SpanEventPipelineOptions.BatchSize"/> or <see cref="SpanEventPipelineOptions.FlushInterval"/>
/// is reached.
/// </summary>
/// <remarks>
/// A deliberate duplicate of <see cref="ClickHouseFlushWorker"/>, not a shared generic
/// base (e.g. a hypothetical <c>RedisBatchFlushWorker&lt;T&gt;</c>). The repo already
/// runs two independent poll-loop <see cref="BackgroundService"/>s
/// (<see cref="ClickHouseFlushWorker"/> and <c>Flare.Api</c>'s <c>AlertEvaluationWorker</c>)
/// without unifying them, and the alerting roadmap item's three notifier channels used
/// per-channel concrete classes rather than an upfront generic notifier abstraction. A
/// third instance of the poll-loop idiom here is consistent with that style, and avoids
/// obscuring likely-divergent batching semantics (span bursts, Nested-column
/// serialization) behind a premature generic base. See <see cref="ClickHouseFlushWorker"/>'s
/// own remarks for the full at-least-once delivery / PEL-reclaim design this mirrors
/// exactly - it is not re-explained here, only reproduced. <see cref="TryDeserialize"/>
/// decodes each entry via <see cref="RedisEventPayload"/> (ADR-0017) - MemoryPack, with a
/// fallback for any pre-upgrade JSON entry still sitting in the stream.
/// </remarks>
public sealed class SpanFlushWorker(
    IConnectionMultiplexer connectionMultiplexer,
    IClickHouseSpanWriter writer,
    IOptions<SpanEventPipelineOptions> options,
    IFlushHealthTracker flushHealth,
    ILogger<SpanFlushWorker> logger) : BackgroundService
{
    private static readonly RedisValue DataField = "data";
    private static readonly RedisValue NewMessages = ">";
    private static readonly RedisValue StreamStart = "0-0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var db = connectionMultiplexer.GetDatabase();

        await EnsureConsumerGroupAsync(db, opts);

        var batch = new List<(RedisValue Id, SpanRecord Span)>(opts.BatchSize);
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
                    await FlushAsync(db, opts, batch);
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

    private static async Task EnsureConsumerGroupAsync(IDatabase db, SpanEventPipelineOptions opts)
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

    private void AppendDeserializable(List<(RedisValue Id, SpanRecord Span)> batch, StreamEntry[] entries)
    {
        foreach (var entry in entries)
        {
            if (TryDeserialize(entry, out var span))
            {
                batch.Add((entry.Id, span));
            }
            // Malformed entries are left un-acked (logged in TryDeserialize) - reclaimed
            // and eventually dropped by ReclaimStalePendingAsync once MaxDeliveryAttempts
            // is exceeded, same as any other failure.
        }
    }

    private bool TryDeserialize(StreamEntry entry, out SpanRecord span)
    {
        var raw = entry[DataField];
        if (raw.IsNullOrEmpty)
        {
            logger.LogWarning("Stream entry {Id} has no {Field} field; will be reclaimed and eventually dropped.", entry.Id, DataField);
            span = null!;
            return false;
        }

        try
        {
            span = RedisEventPayload.Decode((byte[])raw!, SpanEventJsonContext.Default.SpanRecord);
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize stream entry {Id}; will be reclaimed and eventually dropped.", entry.Id);
            span = null!;
            return false;
        }
    }

    /// <summary>
    /// Finds entries idle longer than <see cref="SpanEventPipelineOptions.ReclaimIdle"/>
    /// via <c>XPENDING</c> (the only command that reports each entry's true delivery
    /// count - see <see cref="ClickHouseFlushWorker"/>'s remarks), acks-and-drops the
    /// ones over <see cref="SpanEventPipelineOptions.MaxDeliveryAttempts"/> directly, and
    /// <c>XCLAIM</c>s the rest to fetch their content and fold them into
    /// <paramref name="batch"/> like a freshly read entry.
    /// </summary>
    private async Task ReclaimStalePendingAsync(IDatabase db, SpanEventPipelineOptions opts, List<(RedisValue Id, SpanRecord Span)> batch)
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
                if (TryDeserialize(entry, out var span))
                {
                    batch.Add((entry.Id, span));
                }
            }
        }
    }

    private async Task FlushAsync(IDatabase db, SpanEventPipelineOptions opts, List<(RedisValue Id, SpanRecord Span)> batch)
    {
        var spans = batch.Select(b => b.Span).ToArray();

        try
        {
            await writer.WriteBatchAsync(spans);
            var ids = batch.Select(b => b.Id).ToArray();
            await db.StreamAcknowledgeAsync(opts.StreamKey, opts.ConsumerGroup, ids);
            logger.LogDebug("Flushed {Count} spans to ClickHouse.", spans.Length);
            // IngestionSignal.Traces, not "Spans" - joins on the same vocabulary the
            // Ingestion page's existing OTLP-facing stats already use (see FlushHealthKeys).
            await flushHealth.RecordSuccessAsync(IngestionSignal.Traces, spans.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "ClickHouse flush failed for {Count} entries; leaving un-acked for retry via PEL reclaim.",
                batch.Count);
            // Deliberately do not XACK - entries stay in the PEL and are retried once
            // they age past ReclaimIdle (see ReclaimStalePendingAsync).
            await flushHealth.RecordFailureAsync(IngestionSignal.Traces, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
