using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Stats;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Consumer side of the batched ClickHouse insert pipeline for profile samples: reads
/// <see cref="ProfileSampleRecord"/>s off the Redis Stream <see cref="Sinks.RedisStreamProfileSampleSink"/>
/// writes to, accumulates a batch, and flushes it to ClickHouse once
/// <see cref="ProfileEventPipelineOptions.BatchSize"/> or <see cref="ProfileEventPipelineOptions.FlushInterval"/>
/// is reached.
/// </summary>
/// <remarks>
/// A deliberate duplicate of <see cref="SpanFlushWorker"/> (and through it
/// <see cref="ClickHouseFlushWorker"/>), not a shared generic base - the same call the span and metric
/// workers made. See <see cref="ClickHouseFlushWorker"/>'s remarks for the at-least-once delivery /
/// PEL-reclaim design this reproduces exactly.
/// </remarks>
public sealed class ProfileFlushWorker(
    IConnectionMultiplexer connectionMultiplexer,
    IClickHouseProfileWriter writer,
    IOptions<ProfileEventPipelineOptions> options,
    IFlushHealthTracker flushHealth,
    ILogger<ProfileFlushWorker> logger) : BackgroundService
{
    private static readonly RedisValue DataField = "data";
    private static readonly RedisValue NewMessages = ">";
    private static readonly RedisValue StreamStart = "0-0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var db = connectionMultiplexer.GetDatabase();

        await EnsureConsumerGroupAsync(db, opts);

        var batch = new List<(RedisValue Id, ProfileSampleRecord Sample)>(opts.BatchSize);
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

    private static async Task EnsureConsumerGroupAsync(IDatabase db, ProfileEventPipelineOptions opts)
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

    private void AppendDeserializable(List<(RedisValue Id, ProfileSampleRecord Sample)> batch, StreamEntry[] entries)
    {
        foreach (var entry in entries)
        {
            if (TryDeserialize(entry, out var sample))
            {
                batch.Add((entry.Id, sample));
            }
            // Malformed entries are left un-acked (logged in TryDeserialize) - reclaimed
            // and eventually dropped by ReclaimStalePendingAsync once MaxDeliveryAttempts
            // is exceeded, same as any other failure.
        }
    }

    private bool TryDeserialize(StreamEntry entry, out ProfileSampleRecord sample)
    {
        var raw = entry[DataField];
        if (raw.IsNullOrEmpty)
        {
            logger.LogWarning("Stream entry {Id} has no {Field} field; will be reclaimed and eventually dropped.", entry.Id, DataField);
            sample = null!;
            return false;
        }

        try
        {
            sample = RedisEventPayload.Decode((byte[])raw!, ProfileEventJsonContext.Default.ProfileSampleRecord);
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize stream entry {Id}; will be reclaimed and eventually dropped.", entry.Id);
            sample = null!;
            return false;
        }
    }

    /// <summary>
    /// Finds entries idle longer than <see cref="ProfileEventPipelineOptions.ReclaimIdle"/>
    /// via <c>XPENDING</c> (the only command that reports each entry's true delivery
    /// count - see <see cref="ClickHouseFlushWorker"/>'s remarks), acks-and-drops the
    /// ones over <see cref="ProfileEventPipelineOptions.MaxDeliveryAttempts"/> directly, and
    /// <c>XCLAIM</c>s the rest to fetch their content and fold them into
    /// <paramref name="batch"/> like a freshly read entry.
    /// </summary>
    private async Task ReclaimStalePendingAsync(IDatabase db, ProfileEventPipelineOptions opts, List<(RedisValue Id, ProfileSampleRecord Sample)> batch)
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
                if (TryDeserialize(entry, out var sample))
                {
                    batch.Add((entry.Id, sample));
                }
            }
        }
    }

    private async Task FlushAsync(IDatabase db, ProfileEventPipelineOptions opts, List<(RedisValue Id, ProfileSampleRecord Sample)> batch)
    {
        var samples = batch.Select(b => b.Sample).ToArray();

        try
        {
            await writer.WriteBatchAsync(samples);
            var ids = batch.Select(b => b.Id).ToArray();
            await db.StreamAcknowledgeAsync(opts.StreamKey, opts.ConsumerGroup, ids);
            logger.LogDebug("Flushed {Count} profile samples to ClickHouse.", samples.Length);
            await flushHealth.RecordSuccessAsync(IngestionSignal.Profiles, samples.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "ClickHouse flush failed for {Count} entries; leaving un-acked for retry via PEL reclaim.",
                batch.Count);
            // Deliberately do not XACK - entries stay in the PEL and are retried once
            // they age past ReclaimIdle (see ReclaimStalePendingAsync).
            await flushHealth.RecordFailureAsync(IngestionSignal.Profiles, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
