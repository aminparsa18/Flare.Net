using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Request body for <c>POST /api/spans/search</c>.</summary>
[MemoryPackable]
public sealed partial record SpanSearchRequest
{
    /// <summary>
    /// Same System.Text.Json init-only-property caveat as <c>LogSearchRequest.Filter</c>
    /// - this <c>= new()</c> default doesn't survive deserialization when the JSON body
    /// omits <c>"filter"</c>; <see cref="Query.SpanSearchQueryBuilder"/> coalesces
    /// defensively rather than trust it.
    /// </summary>
    public SpanFilter Filter { get; init; } = new();

    /// <summary>Opaque cursor from a previous <see cref="SpanSearchResponse.NextCursor"/>; omit for the first page.</summary>
    public string? Cursor { get; init; }

    /// <summary>Rows to return. Defaults/caps applied by <see cref="Query.SpanSearchQueryBuilder"/> - see its remarks.</summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Row order - <see cref="SpanSortKey.StartTime"/> (newest first) by default. Appended
    /// after the existing members, not inserted between them, so MemoryPack's positional
    /// wire format stays compatible with clients that only send the first three.
    /// </summary>
    public SpanSortKey SortBy { get; init; }

    /// <summary>
    /// Flips <see cref="SortBy"/> to ascending. Phrased as "ascending" rather than
    /// "descending" so the omitted/default <see langword="false"/> keeps the historical
    /// newest-first (and slowest-first, most-spans-first) order.
    /// </summary>
    public bool SortAscending { get; init; }
}

/// <summary>What <see cref="SpanSearchRequest.SortBy"/> orders by. Every key breaks ties on <c>(TraceId, SpanId)</c>.</summary>
public enum SpanSortKey
{
    /// <summary>The span's own <c>StartTime</c>.</summary>
    StartTime,

    /// <summary>The span's own <c>DurationNano</c> - for a root span, the trace's duration.</summary>
    Duration,

    /// <summary>
    /// Span count of the row's whole trace (<see cref="SpanDto.SpanCount"/>) - computed at
    /// query time, so costlier than the other two: see
    /// <see cref="Query.SpanSearchQueryBuilder"/>'s remarks.
    /// </summary>
    SpanCount,
}

/// <summary>Response body for <c>POST /api/spans/search</c>.</summary>
[MemoryPackable]
public sealed partial record SpanSearchResponse
{
    /// <summary>In <see cref="SpanSearchRequest.SortBy"/> order - most-recent-first (<c>StartTime DESC</c>) by default.</summary>
    public required IReadOnlyList<SpanDto> Spans { get; init; }

    /// <summary>Pass back as the next request's <see cref="SpanSearchRequest.Cursor"/>. Null when this page was the last.</summary>
    public string? NextCursor { get; init; }
}

/// <summary>Response body for <c>GET /api/traces/{traceId}</c> - every span in one trace, for the waterfall view.</summary>
[MemoryPackable]
public sealed partial record TraceDto
{
    public required string TraceId { get; init; }

    /// <summary>Ascending by <c>StartTime</c> - the order a waterfall renders top-to-bottom.</summary>
    public required IReadOnlyList<SpanDto> Spans { get; init; }
}
