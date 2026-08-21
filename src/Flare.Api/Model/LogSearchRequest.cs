namespace Flare.Api.Model;

/// <summary>Request body for <c>POST /api/logs/search</c>.</summary>
public sealed record LogSearchRequest
{
    /// <summary>
    /// The <c>= new()</c> default only applies to callers constructing this in C#
    /// directly - confirmed live that <see cref="System.Text.Json.JsonSerializer"/>
    /// overwrites it back to <see langword="null"/> when a JSON body omits
    /// <c>"filter"</c> entirely (it always assigns init-only properties via
    /// object-initializer during deserialization). <see cref="Query.LogSearchQueryBuilder"/>
    /// coalesces defensively rather than trust this default once JSON is involved.
    /// </summary>
    public LogFilter Filter { get; init; } = new();

    /// <summary>Opaque cursor from a previous <see cref="LogSearchResponse.NextCursor"/>; omit for the first page.</summary>
    public string? Cursor { get; init; }

    /// <summary>Rows to return. Defaults/caps applied by <see cref="Query.LogSearchQueryBuilder"/> - see its remarks.</summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Opt-in: also populate each returned <see cref="LogEventDto.SpanDurationNano"/> via
    /// a bounded follow-up query keyed on this page's own <c>(TraceId, SpanId)</c> pairs
    /// (see <see cref="Query.LogQueryService.SearchAsync"/>). Defaults to
    /// <see langword="false"/> - mirrors <see cref="SpanFilter.RootSpansOnly"/>'s
    /// "only the mode that needs it pays for the follow-up query" principle.
    /// <c>/api/logs/search</c> is also called by Patterns' drill-down
    /// (<c>applyPatternIdFilter</c> in <c>logs/state.svelte.ts</c>) and CSV export
    /// (<c>logs/export.ts</c>), neither of which shows a duration column and shouldn't
    /// pay for this query. Belongs here rather than on <see cref="Filter"/>: like
    /// <see cref="Cursor"/> and <see cref="PageSize"/>, this changes how the response is
    /// shaped, not which rows match. Unlike <see cref="Filter"/>'s default, a plain
    /// non-nullable <see langword="bool"/> needs no defensive re-coalescing against
    /// System.Text.Json's init-property-overwrite behavior - "absent in the request body"
    /// and "explicitly false" both already mean the same thing here.
    /// </summary>
    public bool IncludeSpanDuration { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/search</c>.</summary>
public sealed record LogSearchResponse
{
    /// <summary>Most-recent-first (<c>Timestamp DESC</c>).</summary>
    public required IReadOnlyList<LogEventDto> Events { get; init; }

    /// <summary>Pass back as the next request's <see cref="LogSearchRequest.Cursor"/>. Null when this page was the last.</summary>
    public string? NextCursor { get; init; }
}
