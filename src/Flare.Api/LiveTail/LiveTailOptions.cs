namespace Flare.Api.LiveTail;

/// <summary>
/// Tuning knobs for the live-tail streaming endpoint (<c>GET /api/logs/tail</c>). Bound
/// from the <c>LiveTail</c> configuration section.
/// </summary>
/// <remarks>
/// See <see cref="LogTailBroadcaster"/> - the single background reader that polls
/// <see cref="StreamKey"/> and fans new entries out to every connected WebSocket
/// subscriber's bounded channel (capacity <see cref="SubscriberChannelCapacity"/>).
/// </remarks>
public sealed class LiveTailOptions
{
    public const string SectionName = "LiveTail";

    /// <summary>
    /// The same Redis Stream key <c>Flare.Ingest</c>'s
    /// <c>LogEventPipelineOptions.StreamKey</c> writes into (default <c>"flare:logs"</c>
    /// on both sides) - not project-referenced, so this must be kept in sync by hand if
    /// either side's default or configuration ever changes.
    /// </summary>
    public string StreamKey { get; set; } = "flare:logs";

    /// <summary>
    /// How long to wait between <c>XREAD</c> polls when a read returns no new entries.
    /// Same "no BLOCK parameter" constraint <c>Flare.Ingest</c>'s <c>ClickHouseFlushWorker</c>
    /// already documents for StackExchange.Redis's stream-read bindings.
    /// </summary>
    public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Max entries read per <c>XREAD</c> call.</summary>
    public int ReadBatchSize { get; set; } = 1_000;

    /// <summary>
    /// Bounded capacity of each subscriber's per-connection channel. A subscriber whose
    /// send loop can't keep up has writes fail past this point (see
    /// <see cref="LogTailSubscription"/>'s <c>DropWrite</c> channel mode) rather than
    /// blocking the shared broadcaster loop for every other subscriber.
    /// </summary>
    public int SubscriberChannelCapacity { get; set; } = 500;
}
