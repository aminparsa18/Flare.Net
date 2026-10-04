namespace Flare.Api.Ai;

/// <summary>
/// Bring-your-own-model settings for Flare's AI features - see docs-internal/adr/0103-explain-exception-llm.md.
/// Off by default: with <see cref="Enabled"/> false (or no endpoint/model) no request ever
/// leaves the box and the dashboard hides the action. <see cref="Endpoint"/> is any
/// OpenAI-compatible base URL (OpenAI <c>https://api.openai.com/v1</c>, Ollama
/// <c>http://localhost:11434/v1</c>, vLLM, LiteLLM, ...).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public bool Enabled { get; set; }

    public string Endpoint { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>Bearer token for the endpoint. Optional (local models usually need none).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Hard cap on prompt characters after redaction; the stack trace is truncated first, the source second.</summary>
    public int MaxInputChars { get; set; } = 12_000;

    /// <summary>Completion token budget sent as <c>max_tokens</c>.</summary>
    public int MaxOutputTokens { get; set; } = 800;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Opt-in on top of <see cref="Enabled"/>: write a model summary for every alert that fires
    /// (ADR-0104). Read by <c>Flare.AlertWorker</c>, so it must be set on that service too.
    /// </summary>
    public bool IncidentSummaries { get; set; }

    /// <summary>Cap on summaries generated per clock hour across all rules, so an alert storm can't run up a model bill. Excess alerts keep their plain notification.</summary>
    public int IncidentSummariesPerHour { get; set; } = 20;

    public bool IncidentSummariesActive => IsConfigured && IncidentSummaries;

    public bool IsConfigured => Enabled && Uri.TryCreate(Endpoint, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(Model);
}
