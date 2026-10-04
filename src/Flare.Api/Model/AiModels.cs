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
