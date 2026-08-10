using System.Text.Json;
using Flare.Ingest.Model;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Consumer side of the batched ClickHouse insert pipeline for metrics: reads buffered
/// <see cref="MetricPointRecord"/>s off the single Redis Stream
/// <see cref="Sinks.RedisStreamMetricEventSink"/> writes to, accumulates a batch, and
/// flushes it to ClickHouse once <see cref="MetricEventPipelineOptions.BatchSize"/> or
/// <see cref="MetricEventPipelineOptions.FlushInterval"/> is reached.
/// </summary>
/// <remarks>
/// One shared stream/worker for all three point types (Gauge/Sum/Histogram), not three
/// parallel pipelines the way logs and spans stayed separate from each other. Logs vs.
/// spans were deliberately duplicated because they're different *signals* (see
/// <see cref="SpanFlushWorker"/>'s remarks); Gauge/Sum/Histogram are still one signal -
/// metrics - arriving together in a single OTLP export, so splitting them into three
/// streams/workers/consumer-groups would be tripling operational surface for a
/// sub-shape distinction within one signal, not a signal distinction. At flush time this
/// worker partitions the accumulated batch by runtime type and hands the three lists to
/// <see cref="IClickHouseMetricWriter.WriteBatchAsync"/>, which issues one insert per
/// non-empty table.
///
/// Otherwise a structural duplicate of <see cref="SpanFlushWorker"/> (same
/// poll-loop/XREADGROUP/XPENDING-reclaim/PEL-ack design) - see its remarks for the full
/// at-least-once delivery explanation, not re-explained here.
/// </remarks>
public sealed class MetricFlushWorker(
    IConnectionMultiplexer connectionMultiplexer,
    IClickHouseMetricWriter writer,
    IOptions<MetricEventPipelineOptions> options,
    ILogger<MetricFlushWorker> logger) : BackgroundService
{
    private static readonly RedisValue DataField = "data";
    private static readonly RedisValue NewMessages = ">";
    private static readonly RedisValue StreamStart = "0-0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var db = connectionMultiplexer.GetDatabase();

        await EnsureConsumerGroupAsync(db, opts);

        var batch = new List<(RedisValue Id, MetricPointRecord Point)>(opts.BatchSize);
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

    private static async Task EnsureConsumerGroupAsync(IDatabase db, MetricEventPipelineOptions opts)
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

    private void AppendDeserializable(List<(RedisValue Id, MetricPointRecord Point)> batch, StreamEntry[] entries)
    {
        foreach (var entry in entries)
        {
            if (TryDeserialize(entry, out var point))
            {
                batch.Add((entry.Id, point));
            }
            // Malformed entries are left un-acked (logged in TryDeserialize) - reclaimed
            // and eventually dropped by ReclaimStalePendingAsync once MaxDeliveryAttempts
            // is exceeded, same as any other failure.
        }
    }

    private bool TryDeserialize(StreamEntry entry, out MetricPointRecord point)
    {
        var raw = entry[DataField];
        if (raw.IsNullOrEmpty)
        {
            logger.LogWarning("Stream entry {Id} has no {Field} field; will be reclaimed and eventually dropped.", entry.Id, DataField);
            point = null!;
            return false;
        }

        try
        {
            point = JsonSerializer.Deserialize((string)raw!, MetricEventJsonContext.Default.MetricPointRecord)!;
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize stream entry {Id}; will be reclaimed and eventually dropped.", entry.Id);
            point = null!;
            return false;
        }
    }

    /// <summary>
    /// Finds entries idle longer than <see cref="MetricEventPipelineOptions.ReclaimIdle"/>
    /// via <c>XPENDING</c> (the only command that reports each entry's true delivery
    /// count - see <see cref="ClickHouseFlushWorker"/>'s remarks), acks-and-drops the
    /// ones over <see cref="MetricEventPipelineOptions.MaxDeliveryAttempts"/> directly,
    /// and <c>XCLAIM</c>s the rest to fetch their content and fold them into
    /// <paramref name="batch"/> like a freshly read entry.
    /// </summary>
    private async Task ReclaimStalePendingAsync(IDatabase db, MetricEventPipelineOptions opts, List<(RedisValue Id, MetricPointRecord Point)> batch)
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
                if (TryDeserialize(entry, out var point))
                {
                    batch.Add((entry.Id, point));
                }
            }
        }
    }

    private async Task FlushAsync(IDatabase db, MetricEventPipelineOptions opts, List<(RedisValue Id, MetricPointRecord Point)> batch)
    {
        var gauges = new List<GaugePointRecord>();
        var sums = new List<SumPointRecord>();
        var histograms = new List<HistogramPointRecord>();
        foreach (var (_, point) in batch)
        {
            switch (point)
            {
                case GaugePointRecord gauge:
                    gauges.Add(gauge);
                    break;
                case SumPointRecord sum:
                    sums.Add(sum);
                    break;
                case HistogramPointRecord histogram:
                    histograms.Add(histogram);
                    break;
            }
        }

        try
        {
            await writer.WriteBatchAsync(gauges, sums, histograms);
            var ids = batch.Select(b => b.Id).ToArray();
            await db.StreamAcknowledgeAsync(opts.StreamKey, opts.ConsumerGroup, ids);
            logger.LogDebug(
                "Flushed {Count} metric data points to ClickHouse ({Gauges} gauge, {Sums} sum, {Histograms} histogram).",
                batch.Count, gauges.Count, sums.Count, histograms.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "ClickHouse flush failed for {Count} entries; leaving un-acked for retry via PEL reclaim.",
                batch.Count);
            // Deliberately do not XACK - entries stay in the PEL and are retried once
            // they age past ReclaimIdle (see ReclaimStalePendingAsync).
        }
    }
}
