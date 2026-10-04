using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flare.Api.Json;
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
    IHttpClientFactory httpClientFactory,
    ISourceSnippetService snippets,
    IOptionsMonitor<AiOptions> options,
    ILogger<ExceptionExplainService> logger) : IExceptionExplainService
{
    public const string HttpClientName = "ai-llm";

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

        var body = new ChatCompletionRequest
        {
            Model = config.Model,
            MaxTokens = config.MaxOutputTokens,
            Temperature = 0.2,
            Messages =
            [
                new ChatMessage { Role = "system", Content = ExplainExceptionPromptBuilder.SystemPrompt },
                new ChatMessage { Role = "user", Content = prompt }
            ]
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)));

            using var message = new HttpRequestMessage(HttpMethod.Post, config.Endpoint.TrimEnd('/') + "/chat/completions")
            {
                Content = JsonContent.Create(body, AiJsonContext.Default.ChatCompletionRequest)
            };
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            }

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI endpoint returned {Status} for explain-exception", (int)response.StatusCode);
                return (null, $"The AI endpoint returned {(int)response.StatusCode}.");
            }

            var parsed = await response.Content.ReadFromJsonAsync(AiJsonContext.Default.ChatCompletionResponse, timeout.Token);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            return string.IsNullOrEmpty(text)
                ? (null, "The AI endpoint returned an empty response.")
                : (new ExplainExceptionResponse { Explanation = text, Model = config.Model, IncludedSource = snippet is not null }, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "AI explain-exception call failed");
            return (null, "Could not reach the AI endpoint.");
        }
    }
}
