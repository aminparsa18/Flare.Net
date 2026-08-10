namespace Flare.Ingest.Pipeline;

/// <summary>
/// Tuning knobs for the Redis-Streams-buffered, size/interval-batched ClickHouse insert
/// pipeline for metrics. Bound from the <c>MetricEventPipeline</c> configuration section.
/// </summary>
/// <remarks>
/// See <see cref="Sinks.RedisStreamMetricEventSink"/> (producer, writes to
/// <see cref="StreamKey"/>) and <see cref="MetricFlushWorker"/> (consumer). One shared
/// stream for all three point types (Gauge/Sum/Histogram) - deliberately not three
/// parallel options types the way <see cref="SpanEventPipelineOptions"/> stayed separate
/// from <see cref="LogEventPipelineOptions"/> - see <see cref="MetricFlushWorker"/>'s
/// remarks for the "one signal, not three" reasoning.
/// </remarks>
public sealed class MetricEventPipelineOptions
{
    public const string SectionName = "MetricEventPipeline";

    /// <summary>Redis Stream key that buffers pending metric data points.</summary>
    public string StreamKey { get; set; } = "flare:metrics";

    /// <summary>Consumer group name used for XREADGROUP/XACK at-least-once delivery.</summary>
    public string ConsumerGroup { get; set; } = "flare-ingest-metrics";

    /// <summary>This consumer's name within <see cref="ConsumerGroup"/>. Same v1 single-instance caveat as <see cref="LogEventPipelineOptions.ConsumerName"/>.</summary>
    public string ConsumerName { get; set; } = "flare-ingest-metrics-1";

    /// <summary>Approximate cap on stream length (MAXLEN ~), trimmed on every XADD.</summary>
    public int StreamMaxLength { get; set; } = 1_000_000;

    /// <summary>
    /// Flush once this many unflushed entries have accumulated. Higher default than
    /// <see cref="SpanEventPipelineOptions.BatchSize"/> (2,000) - a single OTLP metrics
    /// export commonly carries many more individual data points per request than a trace
    /// export carries spans (one export can cover dozens of instruments across many
    /// attribute-set timeseries each).
    /// </summary>
    public int BatchSize { get; set; } = 5_000;

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
