using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Ai;

public interface INlFilterService
{
    bool IsEnabled { get; }

    /// <summary>A validated filter proposal, or a user-facing error. Never throws for model/host failures.</summary>
    Task<(NlFilterResponse? Result, string? Error)> GenerateAsync(NlFilterRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// "Natural language to filter" (ADR-0105): redacts the request, asks the configured model for a
/// JSON filter in a fixed vocabulary and returns only what <see cref="NlFilterParser"/> accepted.
/// The model never produces SQL; the dashboard applies the result as ordinary editable chips and
/// the existing query path runs it exactly as if the user had clicked the filter together.
/// </summary>
public sealed class NlFilterService(
    ILlmClient llm,
    IOptionsMonitor<AiOptions> options,
    TimeProvider time,
    ILogger<NlFilterService> logger) : INlFilterService
{
    private const int MaxQueryLength = 500;

    public bool IsEnabled => options.CurrentValue.IsConfigured;

    public async Task<(NlFilterResponse? Result, string? Error)> GenerateAsync(NlFilterRequest request, CancellationToken cancellationToken)
    {
        var config = options.CurrentValue;
        if (!config.IsConfigured)
        {
            return (null, "AI features are not enabled on this Flare instance.");
        }

        if (!NlFilterParser.IsTarget(request.Target) || string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > MaxQueryLength)
        {
            return (null, "Invalid request.");
        }

        var now = time.GetUtcNow();
        var prompt = NlFilterPromptBuilder.Build(request.Target, request.Query, request.KnownServices, now, config.MaxInputChars);
        logger.LogInformation("AI nl-filter ({Target}): sending {Chars} redacted chars to model {Model}", request.Target, prompt.Length, config.Model);
        logger.LogDebug("AI nl-filter prompt: {Prompt}", prompt);

        var (text, error) = await llm.CompleteAsync(NlFilterPromptBuilder.SystemPrompt, prompt, cancellationToken);
        return text is null ? (null, error) : NlFilterParser.Parse(request.Target, text, llm.Model, now);
    }
}
