using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/llm/models</c> - the <c>/llm</c> page's per-model table.
/// Same window shape as <see cref="ExternalDomainsRequest"/>. Carries
/// <c>[GenerateTypeScript]</c> (see <c>Flare.Api.csproj</c>'s MemoryPack TypeScript codegen
/// comment). See docs-internal/adr/0100-llm-observability-genai-spans.md.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record LlmModelsRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.LlmQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c> of the calling span. Null/empty = all services.</summary>
    public string? Service { get; init; }
}

/// <summary>One <c>(provider, model)</c> pair's model-call figures for the window - one row of the <c>/llm</c> page's table.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record LlmModel
{
    /// <summary><c>gen_ai.provider.name</c>, else <c>gen_ai.system</c>, e.g. <c>openai</c>. Empty when the instrumentation set neither.</summary>
    public required string Provider { get; init; }

    /// <summary><c>gen_ai.request.model</c>, else <c>gen_ai.response.model</c>.</summary>
    public required string Model { get; init; }

    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    /// <summary><see cref="CallCount"/> divided by the window length in seconds.</summary>
    public required double PerSecond { get; init; }

    public required double P50Ms { get; init; }

    public required double P95Ms { get; init; }

    public required double P99Ms { get; init; }

    /// <summary>Sum of <c>gen_ai.usage.input_tokens</c> (else <c>prompt_tokens</c>) over the window's calls.</summary>
    public required ulong InputTokens { get; init; }

    /// <summary>Sum of <c>gen_ai.usage.output_tokens</c> (else <c>completion_tokens</c>).</summary>
    public required ulong OutputTokens { get; init; }

    /// <summary>Distinct services that called this model.</summary>
    public required ulong ServiceCount { get; init; }

    public required long LastSeenUnixMs { get; init; }
}

/// <summary>Response body for <c>POST /api/llm/models</c>. Hand-written on the MemoryPack TS side (it holds <see cref="IReadOnlyList{T}"/> fields).</summary>
[MemoryPackable]
public sealed partial record LlmModelsResponse
{
    public required int WindowMinutes { get; init; }

    /// <summary>Busiest first, at most <see cref="Query.LlmQueryBuilder.MaxRows"/>.</summary>
    public required IReadOnlyList<LlmModel> Models { get; init; }

    /// <summary>Every service that made a model call in the window - the toolbar picker, unaffected by the service filter.</summary>
    public required IReadOnlyList<string> Services { get; init; }
}
