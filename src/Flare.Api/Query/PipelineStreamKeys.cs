using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Maps each <see cref="IngestionSignal"/> to the Redis Stream key + consumer-group name
/// <c>Flare.Ingest</c>'s corresponding flush worker reads from, by <b>default</b> -
/// <c>LogEventPipelineOptions</c>/<c>SpanEventPipelineOptions</c>/<c>MetricEventPipelineOptions</c>'
/// own default <c>StreamKey</c>/<c>ConsumerGroup</c> values, hand-duplicated here rather than
/// referenced (see <see cref="IngestionSignal"/>'s remarks on why there's no project
/// reference between Flare.Api and Flare.Ingest).
///
/// <see cref="IngestionSignal.Traces"/> deliberately maps to the <c>"flare:spans"</c> stream
/// and <c>"flare-ingest-spans"</c> group - the pipeline layer calls this signal "spans"
/// (<c>SpanFlushWorker</c>, <c>SpanEventPipelineOptions</c>), while the OTLP-facing stats
/// layer (this enum, v8) calls it "Traces". This class is the one place that bridges the
/// two vocabularies for the pipeline-health feature (v10).
///
/// <b>Known limitation, deliberately not solved here</b>: these are Flare.Ingest's
/// *configured defaults*, not read from shared config. A deployment that overrides
/// <c>StreamKey</c>/<c>ConsumerGroup</c> via its own config/env vars won't have its stream
/// health picked up by <see cref="PipelineQueryService"/> - it has no way to discover the
/// override, since the two processes share no config source today. Flagged, not fixed,
/// same "flag rather than fully solve" precedent v6's rate-calc/quantile-interpolation
/// limitations use.
/// </summary>
public static class PipelineStreamKeys
{
    public static string StreamKey(IngestionSignal signal) => signal switch
    {
        IngestionSignal.Logs => "flare:logs",
        IngestionSignal.Traces => "flare:spans",
        IngestionSignal.Metrics => "flare:metrics",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };

    public static string ConsumerGroup(IngestionSignal signal) => signal switch
    {
        IngestionSignal.Logs => "flare-ingest",
        IngestionSignal.Traces => "flare-ingest-spans",
        IngestionSignal.Metrics => "flare-ingest-metrics",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };
}
