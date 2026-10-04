using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.AiEndpoints"/> - camelCase, same convention as <see cref="SourceLinksJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AiStatusResponse))]
[JsonSerializable(typeof(ExplainExceptionRequest))]
[JsonSerializable(typeof(ExplainExceptionResponse))]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
public sealed partial class AiJsonContext : JsonSerializerContext;

/// <summary>Minimal OpenAI-compatible <c>/chat/completions</c> wire shapes (snake_case property names set explicitly).</summary>
public sealed record ChatCompletionRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }

    [JsonPropertyName("messages")] public required IReadOnlyList<ChatMessage> Messages { get; init; }

    [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }

    [JsonPropertyName("temperature")] public double Temperature { get; init; }
}

public sealed record ChatMessage
{
    [JsonPropertyName("role")] public required string Role { get; init; }

    [JsonPropertyName("content")] public string? Content { get; init; }
}

public sealed record ChatCompletionResponse
{
    [JsonPropertyName("choices")] public IReadOnlyList<ChatChoice>? Choices { get; init; }
}

public sealed record ChatChoice
{
    [JsonPropertyName("message")] public ChatMessage? Message { get; init; }
}
