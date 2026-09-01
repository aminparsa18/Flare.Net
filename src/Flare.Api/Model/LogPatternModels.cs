using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Request body for <c>POST /api/logs/patterns</c> - the ranked Drain-cluster view.</summary>
[MemoryPackable]
public sealed partial record LogPatternRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public LogFilter Filter { get; init; } = new();

    /// <summary>Max rows to return, ranked by <see cref="LogPatternRow.Count"/> descending. Clamped server-side (see <see cref="Query.LogPatternQueryBuilder"/>); null uses the default.</summary>
    public int? TopN { get; init; }
}

/// <summary>One ranked pattern: a Drain cluster and its occurrence stats within the request's filter window.</summary>
[MemoryPackable]
public sealed partial record LogPatternRow
{
    public required string PatternId { get; init; }

    /// <summary>The (possibly wildcarded, <c>&lt;*&gt;</c>) template text, e.g. "GET /api/orders/&lt;*&gt;".</summary>
    public required string Template { get; init; }

    public required long Count { get; init; }

    /// <summary>Rows within this cluster whose <c>SeverityNumber</c> is at least OTel's ERROR floor (17).</summary>
    public required long ErrorCount { get; init; }

    public required DateTimeOffset FirstSeen { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/patterns</c>.</summary>
[MemoryPackable]
public sealed partial record LogPatternResponse
{
    public required IReadOnlyList<LogPatternRow> Patterns { get; init; }
}
