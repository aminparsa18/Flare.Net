namespace Flare.Api.Pipeline;

/// <summary>
/// Read-side mirror of <c>Flare.Ingest.Pipeline.ProfileEventPipelineOptions</c>, bound from
/// the same <c>ProfileEventPipeline</c> configuration section - see
/// <see cref="LogEventPipelineOptions"/>'s remarks for why this exists and what it doesn't
/// mirror.
/// </summary>
public sealed class ProfileEventPipelineOptions
{
    public const string SectionName = "ProfileEventPipeline";

    /// <summary>Redis Stream key that buffers pending profile samples. Must match Flare.Ingest's.</summary>
    public string StreamKey { get; set; } = "flare:profiles";

    /// <summary>Consumer group Flare.Ingest's <c>ProfileFlushWorker</c> reads via <c>XREADGROUP</c>/<c>XACK</c>. Must match Flare.Ingest's.</summary>
    public string ConsumerGroup { get; set; } = "flare-ingest-profiles";

    /// <summary>Approximate cap on stream length (MAXLEN ~) - the denominator behind the buffer-utilization display. Must match Flare.Ingest's.</summary>
    public int StreamMaxLength { get; set; } = 1_000_000;
}
