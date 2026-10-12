namespace Flare.Ingest.Pipeline;

/// <summary>
/// Tuning knobs for the Redis-Streams-buffered, batched ClickHouse insert pipeline for profile
/// samples. Bound from the <c>ProfileEventPipeline</c> configuration section. A separate options
/// type from <see cref="SpanEventPipelineOptions"/> for the same duplicate-the-pipeline reason as
/// <see cref="ProfileFlushWorker"/>.
/// </summary>
public sealed class ProfileEventPipelineOptions
{
    public const string SectionName = "ProfileEventPipeline";

    /// <summary>Redis Stream key that buffers pending profile samples.</summary>
    public string StreamKey { get; set; } = "flare:profiles";

    /// <summary>Consumer group name used for XREADGROUP/XACK at-least-once delivery.</summary>
    public string ConsumerGroup { get; set; } = "flare-ingest-profiles";

    public string ConsumerName { get; set; } = $"flare-ingest-profiles-{ConsumerIdentity.Suffix}";

    /// <summary>Approximate cap on stream length (MAXLEN ~), trimmed on every XADD.</summary>
    public int StreamMaxLength { get; set; } = 1_000_000;

    /// <summary>Flush once this many unflushed samples have accumulated. One profile export is typically hundreds to thousands of samples.</summary>
    public int BatchSize { get; set; } = 5_000;

    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan ReclaimIdle { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ReclaimInterval { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxDeliveryAttempts { get; set; } = 5;
}
