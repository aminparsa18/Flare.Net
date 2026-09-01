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
}

/// <summary>Response body for <c>POST /api/spans/search</c>.</summary>
[MemoryPackable]
public sealed partial record SpanSearchResponse
{
    /// <summary>Most-recent-first (<c>StartTime DESC</c>).</summary>
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
