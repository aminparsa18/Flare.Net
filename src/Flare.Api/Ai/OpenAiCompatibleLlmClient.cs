using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flare.Api.Json;
using Microsoft.Extensions.Options;

namespace Flare.Api.Ai;

/// <summary>The one seam to the configured model, shared by every AI feature (ADR-0103, ADR-0104).</summary>
public interface ILlmClient
{
    /// <summary>True when <c>Ai__Enabled</c> and an endpoint and model are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>The configured model name, for recording next to what it produced.</summary>
    string Model { get; }

    /// <summary>The model's answer, or a user-facing error. Never throws for model/host failures.</summary>
    Task<(string? Text, string? Error)> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

/// <summary>
/// One non-streaming OpenAI-compatible <c>POST {Endpoint}/chat/completions</c> (OpenAI, Ollama, vLLM,
/// LiteLLM). Bounded by <see cref="AiOptions.MaxOutputTokens"/> and its own timeout; redirects are not followed.
/// Callers redact and size-bound the prompt before it gets here.
/// </summary>
public sealed class OpenAiCompatibleLlmClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<AiOptions> options,
    ILogger<OpenAiCompatibleLlmClient> logger) : ILlmClient
{
    public const string HttpClientName = "ai-llm";

    public bool IsConfigured => options.CurrentValue.IsConfigured;

    public string Model => options.CurrentValue.Model;

    public async Task<(string? Text, string? Error)> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var config = options.CurrentValue;
        if (!config.IsConfigured)
        {
            return (null, "AI features are not enabled on this Flare instance.");
        }

        var body = new ChatCompletionRequest
        {
            Model = config.Model,
            MaxTokens = config.MaxOutputTokens,
            Temperature = 0.2,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userPrompt }
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
                logger.LogWarning("AI endpoint returned {Status}", (int)response.StatusCode);
                return (null, $"The AI endpoint returned {(int)response.StatusCode}.");
            }

            var parsed = await response.Content.ReadFromJsonAsync(AiJsonContext.Default.ChatCompletionResponse, timeout.Token);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            return string.IsNullOrEmpty(text)
                ? (null, "The AI endpoint returned an empty response.")
                : (text, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Caller-initiated cancellation (shutdown) must still propagate rather than read as a model failure.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(ex, "AI call failed");
            return (null, "Could not reach the AI endpoint.");
        }
    }
}
