namespace Flare.Ingest.Pipeline;

/// <summary>
/// Tuning knobs for the Redis-Streams-buffered, size/interval-batched ClickHouse insert
/// pipeline. Bound from the <c>LogEventPipeline</c> configuration section.
/// </summary>
/// <remarks>
/// See <see cref="Sinks.RedisStreamLogEventSink"/> (producer, writes to <see cref="StreamKey"/>)
/// and <see cref="ClickHouseFlushWorker"/> (consumer, reads via <see cref="ConsumerGroup"/>/
/// <see cref="ConsumerName"/> and flushes once <see cref="BatchSize"/> or
/// <see cref="FlushInterval"/> is reached).
/// </remarks>
public sealed class LogEventPipelineOptions
{
    public const string SectionName = "LogEventPipeline";

    /// <summary>Redis Stream key that buffers pending log events.</summary>
    public string StreamKey { get; set; } = "flare:logs";

    /// <summary>Consumer group name used for XREADGROUP/XACK at-least-once delivery.</summary>
    public string ConsumerGroup { get; set; } = "flare-ingest";

    /// <summary>
    /// This consumer's name within <see cref="ConsumerGroup"/>. Fixed for v1's
    /// single-instance deployment model - running multiple concurrent Flare.Ingest
    /// replicas would need unique names per instance (e.g. derived from machine/pod
    /// name) to avoid falsely reclaiming each other's in-flight work. Not solved here;
    /// a Later-item if/when Flare.Ingest becomes horizontally scaled.
    /// </summary>
    public string ConsumerName { get; set; } = "flare-ingest-1";

    /// <summary>
    /// Approximate cap on stream length (MAXLEN ~), trimmed on every XADD, so Redis
    /// memory stays bounded if ClickHouse is unreachable for a long time. <c>int</c> to
    /// match StackExchange.Redis's <c>StreamAddAsync(maxLength: int?)</c> parameter.
    /// </summary>
    public int StreamMaxLength { get; set; } = 1_000_000;

    /// <summary>Flush once this many unflushed entries have accumulated.</summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>Flush at least this often even if <see cref="BatchSize"/> hasn't been reached.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long to wait between XREADGROUP polls when a read returns no new entries.
    /// StackExchange.Redis's XREADGROUP has no BLOCK parameter, so the consumer loop
    /// polls rather than blocking on the connection.
    /// </summary>
    public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long an entry must sit un-acked in the consumer group's pending-entries list
    /// (PEL) before it's eligible for reclaim - covers both a crash mid-flush (entry
    /// never acked) and a deliberate no-ack-on-failure retry. Should comfortably exceed
    /// one flush cycle.
    /// </summary>
    public TimeSpan ReclaimIdle { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the worker scans the PEL for reclaimable entries.</summary>
    public TimeSpan ReclaimInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Poison-message safety net: once an entry's delivery count exceeds this, it's
    /// logged and dropped (acked without a ClickHouse insert) instead of retried
    /// forever. A bounded skip-and-log, not a full dead-letter subsystem - that's a
    /// Later-item if it turns out to be needed.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;
}
