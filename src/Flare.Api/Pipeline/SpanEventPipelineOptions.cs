namespace Flare.Api.Pipeline;

/// <summary>
/// Read-side mirror of <c>Flare.Ingest.Pipeline.SpanEventPipelineOptions</c>, bound from the
/// same <c>SpanEventPipeline</c> configuration section - see
/// <see cref="LogEventPipelineOptions"/>'s remarks for why this exists and what it doesn't
/// mirror.
///
/// <c>IngestionSignal.Traces</c> deliberately maps to this "spans" pipeline - the pipeline
/// layer calls this signal "spans" (<c>SpanFlushWorker</c>, this type), while the OTLP-facing
/// stats layer (<c>IngestionSignal</c>, v8) calls it "Traces". Unrelated to the config-drift
/// bug this type fixes; unchanged by it.
/// </summary>
public sealed class SpanEventPipelineOptions
{
    public const string SectionName = "SpanEventPipeline";

    /// <summary>
    /// Redis Stream key that buffers pending spans. Must match Flare.Ingest's
    /// <c>SpanEventPipelineOptions.StreamKey</c>.
    /// </summary>
    public string StreamKey { get; set; } = "flare:spans";

    /// <summary>
    /// Consumer group Flare.Ingest's <c>SpanFlushWorker</c> reads via
    /// <c>XREADGROUP</c>/<c>XACK</c>. Must match Flare.Ingest's
    /// <c>SpanEventPipelineOptions.ConsumerGroup</c>.
    /// </summary>
    public string ConsumerGroup { get; set; } = "flare-ingest-spans";
}
