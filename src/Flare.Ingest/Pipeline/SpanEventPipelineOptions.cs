namespace Flare.Ingest.Pipeline;

/// <summary>
/// Tuning knobs for the Redis-Streams-buffered, size/interval-batched ClickHouse insert
/// pipeline for spans. Bound from the <c>SpanEventPipeline</c> configuration section.
/// </summary>
/// <remarks>
/// See <see cref="Sinks.RedisStreamSpanEventSink"/> (producer, writes to <see cref="StreamKey"/>)
/// and <see cref="SpanFlushWorker"/> (consumer). Deliberately a separate options type
/// from <see cref="LogEventPipelineOptions"/>, not a shared/generic one - same
/// duplicate-the-pipeline call as <see cref="SpanFlushWorker"/> itself; see its remarks.
/// </remarks>
public sealed class SpanEventPipelineOptions
{
    public const string SectionName = "SpanEventPipeline";

    /// <summary>Redis Stream key that buffers pending spans.</summary>
    public string StreamKey { get; set; } = "flare:spans";

    /// <summary>Consumer group name used for XREADGROUP/XACK at-least-once delivery.</summary>
    public string ConsumerGroup { get; set; } = "flare-ingest-spans";

    /// <summary>This consumer's name within <see cref="ConsumerGroup"/>. Same machine/process-derived default as <see cref="LogEventPipelineOptions.ConsumerName"/>.</summary>
    public string ConsumerName { get; set; } = $"flare-ingest-spans-{ConsumerIdentity.Suffix}";

    /// <summary>Approximate cap on stream length (MAXLEN ~), trimmed on every XADD.</summary>
    public int StreamMaxLength { get; set; } = 1_000_000;

    /// <summary>
    /// Flush once this many unflushed entries have accumulated. Higher default than
    /// <see cref="LogEventPipelineOptions.BatchSize"/> (1,000) since a single OTLP trace
    /// export commonly carries hundreds of spans per request - a lower batch size would
    /// mean flushing multiple times per incoming export under normal, not bursty, load.
    /// </summary>
    public int BatchSize { get; set; } = 2_000;

    /// <summary>Flush at least this often even if <see cref="BatchSize"/> hasn't been reached.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait between XREADGROUP polls when a read returns no new entries.</summary>
    public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long an entry must sit un-acked in the PEL before it's eligible for reclaim.</summary>
    public TimeSpan ReclaimIdle { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the worker scans the PEL for reclaimable entries.</summary>
    public TimeSpan ReclaimInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Poison-message safety net: entries exceeding this delivery count are logged and dropped.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;
}
