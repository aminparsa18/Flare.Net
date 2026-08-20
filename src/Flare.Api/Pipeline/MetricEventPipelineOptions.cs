namespace Flare.Api.Pipeline;

/// <summary>
/// Read-side mirror of <c>Flare.Ingest.Pipeline.MetricEventPipelineOptions</c>, bound from
/// the same <c>MetricEventPipeline</c> configuration section - see
/// <see cref="LogEventPipelineOptions"/>'s remarks for why this exists and what it doesn't
/// mirror.
/// </summary>
public sealed class MetricEventPipelineOptions
{
    public const string SectionName = "MetricEventPipeline";

    /// <summary>
    /// Redis Stream key that buffers pending metric points. Must match Flare.Ingest's
    /// <c>MetricEventPipelineOptions.StreamKey</c>.
    /// </summary>
    public string StreamKey { get; set; } = "flare:metrics";

    /// <summary>
    /// Consumer group Flare.Ingest's <c>MetricFlushWorker</c> reads via
    /// <c>XREADGROUP</c>/<c>XACK</c>. Must match Flare.Ingest's
    /// <c>MetricEventPipelineOptions.ConsumerGroup</c>.
    /// </summary>
    public string ConsumerGroup { get; set; } = "flare-ingest-metrics";

    /// <summary>
    /// Approximate cap on stream length (MAXLEN ~). Must match Flare.Ingest's
    /// <c>MetricEventPipelineOptions.StreamMaxLength</c> - see
    /// <see cref="LogEventPipelineOptions.StreamMaxLength"/>'s remarks for what this is used
    /// for and why staleness only degrades the displayed percentage, not correctness.
    /// </summary>
    public int StreamMaxLength { get; set; } = 1_000_000;
}
