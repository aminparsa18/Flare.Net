namespace Flare.Api.Model;

/// <summary>Response for <c>GET /api/ai/status</c>: lets the dashboard hide AI actions unless an admin configured a model (ADR-0103).</summary>
public sealed record AiStatusResponse
{
    public required bool Enabled { get; init; }
}

/// <summary>
/// Request for <c>POST /api/ai/explain-exception</c>. The server fetches <see cref="Source"/>
/// itself (through the same bounded path as the inline-source feature) rather than accepting
/// client-supplied source text.
/// </summary>
public sealed record ExplainExceptionRequest
{
    public required string ServiceName { get; init; }

    public required string ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? Stacktrace { get; init; }

    public SourceSnippetRequest? Source { get; init; }
}

public sealed record ExplainExceptionResponse
{
    public required string Explanation { get; init; }

    public required string Model { get; init; }

    /// <summary>Whether the throw-site source made it into the prompt.</summary>
    public required bool IncludedSource { get; init; }
}

/// <summary>Request for <c>POST /api/ai/nl-filter</c> (ADR-0105). <see cref="Target"/> is <c>Logs</c> or <c>Traces</c>.</summary>
public sealed record NlFilterRequest
{
    public required string Target { get; init; }

    public required string Query { get; init; }

    /// <summary>Service names the explorer already knows, so the model can map "checkout" to the real <c>checkout-api</c> instead of guessing.</summary>
    public IReadOnlyList<string>? KnownServices { get; init; }
}

/// <summary>One attribute condition in an <see cref="NlFilterResponse"/>; the same shape the explorers' attribute chips use.</summary>
public sealed record NlAttributeFilter
{
    public required string Bag { get; init; }

    public required string Key { get; init; }

    public required string Value { get; init; }

    public required string Operator { get; init; }

    public IReadOnlyList<string>? Values { get; init; }
}

public sealed record NlCustomRange
{
    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }
}

/// <summary>
/// A model-proposed filter, already whitelisted and validated by <c>NlFilterParser</c>. Field names
/// match the dashboard's saved-view state so it applies through the normal restore path and
/// shows up as ordinary editable chips. Never contains SQL.
/// </summary>
public sealed record NlFilterResponse
{
    public required string Model { get; init; }

    public required string TimeRangePreset { get; init; }

    public NlCustomRange? CustomRange { get; init; }

    public required IReadOnlyList<string> Services { get; init; }

    public required IReadOnlyList<int> SeverityNumbers { get; init; }

    public required string Search { get; init; }

    public required IReadOnlyList<NlAttributeFilter> AttributeFilters { get; init; }

    public required IReadOnlyList<string> StatusCodes { get; init; }

    public TraceStructureFilter? Structure { get; init; }

    /// <summary>Parts of the request the model asked for that were dropped (unsupported, invalid or out of range), shown to the user.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
