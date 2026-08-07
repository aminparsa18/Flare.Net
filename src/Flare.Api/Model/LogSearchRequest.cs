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
}

/// <summary>Response body for <c>POST /api/logs/search</c>.</summary>
public sealed record LogSearchResponse
{
    /// <summary>Most-recent-first (<c>Timestamp DESC</c>).</summary>
    public required IReadOnlyList<LogEventDto> Events { get; init; }

    /// <summary>Pass back as the next request's <see cref="LogSearchRequest.Cursor"/>. Null when this page was the last.</summary>
    public string? NextCursor { get; init; }
}
