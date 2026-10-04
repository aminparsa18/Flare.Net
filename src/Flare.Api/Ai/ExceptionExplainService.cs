using Flare.Api.Model;
using Flare.Api.Source;
using Microsoft.Extensions.Options;

namespace Flare.Api.Ai;

public interface IExceptionExplainService
{
    bool IsEnabled { get; }

    /// <summary>The model's explanation, or a user-facing error. Never throws for model/host/source failures.</summary>
    Task<(ExplainExceptionResponse? Result, string? Error)> ExplainAsync(ExplainExceptionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// "Explain this exception" (ADR-0103): redacts the exception, stack trace and throw-site source,
/// bounds the prompt, and asks the configured OpenAI-compatible model for an explanation. The
/// exact redacted prompt is logged so what left the box is recorded.
/// </summary>
public sealed class ExceptionExplainService(
    ILlmClient llm,
    ISourceSnippetService snippets,
    IOptionsMonitor<AiOptions> options,
    ILogger<ExceptionExplainService> logger) : IExceptionExplainService
{
    public bool IsEnabled => options.CurrentValue.IsConfigured;

    public async Task<(ExplainExceptionResponse? Result, string? Error)> ExplainAsync(ExplainExceptionRequest request, CancellationToken cancellationToken)
    {
        var config = options.CurrentValue;
        if (!config.IsConfigured)
        {
            return (null, "AI features are not enabled on this Flare instance.");
        }

        if (request.ServiceName.Length > 200 || request.ExceptionType.Length > 500)
        {
            return (null, "Invalid request.");
        }

        SourceSnippetResponse? snippet = null;
        if (request.Source is not null)
        {
            // A source failure is not fatal: the explanation just goes without the source.
            (snippet, _) = await snippets.GetAsync(request.Source, cancellationToken);
        }

        var prompt = ExplainExceptionPromptBuilder.Build(request, request.Source?.Path, snippet?.StartLine ?? 0, snippet?.Lines, config.MaxInputChars);
        logger.LogInformation(
            "AI explain-exception for {Service} {ExceptionType}: sending {Chars} redacted chars to model {Model} (source included: {IncludedSource})",
            request.ServiceName, request.ExceptionType, prompt.Length, config.Model, snippet is not null);
        logger.LogDebug("AI explain-exception prompt: {Prompt}", prompt);

        var (text, error) = await llm.CompleteAsync(ExplainExceptionPromptBuilder.SystemPrompt, prompt, cancellationToken);
        return text is null
            ? (null, error)
            : (new ExplainExceptionResponse { Explanation = text, Model = llm.Model, IncludedSource = snippet is not null }, null);
    }
}
