using Flare.Api.Model;
using Flare.Api.Pipeline;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>
/// Resolves each <see cref="IngestionSignal"/>'s Redis Stream key + consumer-group name from
/// Flare.Api's own bound copies of Flare.Ingest's pipeline options
/// (<see cref="Pipeline.LogEventPipelineOptions"/>/<see cref="Pipeline.SpanEventPipelineOptions"/>/
/// <see cref="Pipeline.MetricEventPipelineOptions"/>) instead of hardcoded literals - see
/// those types' remarks for why this now lets a <c>LogEventPipeline__StreamKey</c> (etc.)
/// env-var override actually reach <see cref="PipelineQueryService"/>, as long as it's
/// applied to Flare.Api's environment too (the two processes still share no config source -
/// same "different deployables, no reference" precedent every other Flare.Ingest/Flare.Api
/// pairing in this repo already follows).
///
/// <see cref="IngestionSignal.Traces"/> still deliberately maps to the "spans" pipeline
/// (<see cref="Pipeline.SpanEventPipelineOptions"/>) - the vocabulary bridge this class
/// already provided (OTLP-facing "Traces" vs. pipeline-facing "spans") is unchanged by this
/// fix; only the *source* of the values changed, from hardcoded literals to config binding.
///
/// Registered as a singleton (<c>Program.cs</c>) and injected wherever a signal's stream
/// key/consumer group is needed - <see cref="PipelineQueryService"/> today.
/// </summary>
public sealed class PipelineStreamKeys(
    IOptions<LogEventPipelineOptions> logOptions,
    IOptions<SpanEventPipelineOptions> spanOptions,
    IOptions<MetricEventPipelineOptions> metricOptions)
{
    public string StreamKey(IngestionSignal signal) => signal switch
    {
        IngestionSignal.Logs => logOptions.Value.StreamKey,
        IngestionSignal.Traces => spanOptions.Value.StreamKey,
        IngestionSignal.Metrics => metricOptions.Value.StreamKey,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };

    public string ConsumerGroup(IngestionSignal signal) => signal switch
    {
        IngestionSignal.Logs => logOptions.Value.ConsumerGroup,
        IngestionSignal.Traces => spanOptions.Value.ConsumerGroup,
        IngestionSignal.Metrics => metricOptions.Value.ConsumerGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };

    /// <summary>Approximate MAXLEN cap for this signal's stream - the denominator behind the Ingestion page's buffer-utilization display.</summary>
    public long Capacity(IngestionSignal signal) => signal switch
    {
        IngestionSignal.Logs => logOptions.Value.StreamMaxLength,
        IngestionSignal.Traces => spanOptions.Value.StreamMaxLength,
        IngestionSignal.Metrics => metricOptions.Value.StreamMaxLength,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };
}
