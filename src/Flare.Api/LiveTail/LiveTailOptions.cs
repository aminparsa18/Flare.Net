namespace Flare.Api.LiveTail;

/// <summary>
/// Tuning knobs for the live-tail streaming endpoint (<c>GET /api/logs/tail</c>). Bound
/// from the <c>LiveTail</c> configuration section.
/// </summary>
/// <remarks>
/// See <see cref="LogTailBroadcaster"/> - the single background reader that polls
/// <c>Pipeline.LogEventPipelineOptions.StreamKey</c> (the same <c>LogEventPipeline</c>
/// config section Flare.Ingest binds - see that type's remarks; this class used to carry
/// its own hand-synced <c>StreamKey</c> bound from this file's unrelated <c>LiveTail</c>
/// section, which was a second, independent instance of the same config-drift bug
/// <c>Query.PipelineStreamKeys</c> fixed) and fans new entries out to every connected
/// WebSocket subscriber's bounded channel (capacity <see cref="SubscriberChannelCapacity"/>).
/// </remarks>
public sealed class LiveTailOptions
{
    public const string SectionName = "LiveTail";

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

    /// <summary>
    /// Largest client-to-server message (summed across fragments) the receive loop accepts; past
    /// it the socket is closed with 1009. Control messages are small JSON, so 64 KB is generous.
    /// </summary>
    public int MaxClientMessageBytes { get; set; } = 64 * 1024;

    /// <summary>Max concurrent live-tail sockets per user (anonymous callers share one bucket). Past it the upgrade is refused with 429.</summary>
    public int MaxConnectionsPerUser { get; set; } = 10;

    /// <summary>Max concurrent live-tail sockets across all users. Past it the upgrade is refused with 429.</summary>
    public int MaxConnections { get; set; } = 200;

    /// <summary>
    /// Longest a live-tail, resource-graph or host-stats socket stays open before the server closes it
    /// and the client reconnects. Authorization is only checked at upgrade, so this bounds how long a
    /// revoked session, disabled user, expired PAT or removed project membership keeps streaming.
    /// Zero or negative disables the cap.
    /// </summary>
    public TimeSpan MaxSocketLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Max entries in one subscription's filter: services, severities, scope names, attribute and body-JSON filters, and their in/not-in values combined.</summary>
    public int MaxFilterItems { get; set; } = 100;

    /// <summary>Max length of any single string in a subscription's filter (search text, values, keys, paths).</summary>
    public int MaxFilterStringLength { get; set; } = 1_024;

    /// <summary>Max <c>regex</c>/<c>not-regex</c> operators in one subscription's filter. Each is compiled once at subscribe time.</summary>
    public int MaxRegexPatterns { get; set; } = 10;

    /// <summary>Max length of one regex pattern.</summary>
    public int MaxRegexPatternLength { get; set; } = 256;

    /// <summary>Per-match timeout for subscription regexes. A match that exceeds it counts as no match.</summary>
    public TimeSpan RegexMatchTimeout { get; set; } = TimeSpan.FromMilliseconds(50);
}
