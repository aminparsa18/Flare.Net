using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/logs/context</c> - the Logs "context" view: given one
/// specific event (identified by its <see cref="Timestamp"/>/<see cref="EventId"/> sort
/// key, same tuple <see cref="Query.LogSearchCursor"/> already uses), fetch the events
/// immediately chronologically before/after it.
/// </summary>
/// <remarks>
/// Deliberately carries no <see cref="LogFilter"/>: this is meant to answer "what else
/// was happening right around this log line", not "what else matches the search that
/// found it" - the same reasoning that makes it a shareable permalink (see
/// docs-internal/planning/roadmap.md's former "Logs context view" entry, and SigNoz's
/// prior art it cites) rather than something that only makes sense alongside the
/// original filter state. Unfiltered also means it doesn't inherit
/// <see cref="Query.LogFilterSqlBuilder.DefaultLookback"/>'s now-relative 1-hour default
/// window, which would silently break a context request for an old event -
/// <see cref="Query.LogContextQueryBuilder"/> builds its own SQL directly rather than
/// going through <see cref="Query.LogFilterSqlBuilder"/> for this reason.
/// </remarks>
[MemoryPackable]
public sealed partial record LogContextRequest
{
    /// <summary>The anchor event's <c>EventId</c> - together with <see cref="Timestamp"/>, its exact position in the <c>(Timestamp, EventId)</c> sort key.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The anchor event's <c>Timestamp</c>, exactly as returned by a prior search/tail (not re-parsed/rounded) - see <see cref="Query.LogContextQueryBuilder"/>'s remarks on why an approximate value would miss the row.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Events to fetch strictly before the anchor. Defaults/caps applied by <see cref="Query.LogContextQueryBuilder"/>.</summary>
    public int? Before { get; init; }

    /// <summary>Events to fetch strictly after the anchor. Defaults/caps applied by <see cref="Query.LogContextQueryBuilder"/>.</summary>
    public int? After { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/context</c>.</summary>
[MemoryPackable]
public sealed partial record LogContextResponse
{
    /// <summary>
    /// Most-recent-first (<c>Timestamp DESC</c>), same convention as
    /// <see cref="LogSearchResponse.Events"/>: up to <c>Before</c> older events, then the
    /// anchor event itself (identify it by <see cref="AnchorEventId"/> - see that
    /// property's remarks on why it can be absent), then up to <c>After</c> newer events.
    /// </summary>
    public required IReadOnlyList<LogEventDto> Events { get; init; }

    /// <summary>
    /// Echoes the request's <see cref="LogContextRequest.EventId"/> so the caller can find
    /// the anchor row in <see cref="Events"/> to highlight/scroll to it - it is not
    /// necessarily present there. A log's retention policy can delete the exact row
    /// between when a permalink was created and when it's opened while its neighbors (just
    /// outside the deleted range) remain, so a missing anchor isn't an error case the
    /// caller needs to special-case beyond "nothing to highlight".
    /// </summary>
    public required Guid AnchorEventId { get; init; }

    /// <summary>True if more events exist strictly before the oldest row in <see cref="Events"/>.</summary>
    public bool HasMoreBefore { get; init; }

    /// <summary>True if more events exist strictly after the newest row in <see cref="Events"/>.</summary>
    public bool HasMoreAfter { get; init; }
}
