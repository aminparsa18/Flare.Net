using Microsoft.Extensions.AI;

namespace ExampleApp.Shop;

/// <summary>
/// The shop's model calls - fraud-check explains its score with a chat model, notification-service
/// drafts the order SMS with another, and the storefront rewrites search queries with a self-hosted
/// model and embeds them. The clients are
/// real Microsoft.Extensions.AI pipelines (<c>UseOpenTelemetry()</c> on top), so the spans and
/// metrics they emit are exactly what an app calling OpenAI or Anthropic would send; only the
/// model underneath is an in-process fake, so the demo needs no API key, no network and no extra
/// container.
/// </summary>
public static class LlmClients
{
    /// <summary>The ActivitySource/Meter name given to <c>UseOpenTelemetry(sourceName: ...)</c> - subscribed in Program.cs.</summary>
    public const string InstrumentationName = "Microsoft.Extensions.AI";

    public const string FraudModel = "gpt-4o-mini";
    public const string NotificationModel = "claude-haiku-4-5";
    /// <summary>A self-hosted model: no built-in price, so the /llm page shows "No price" until an admin sets one.</summary>
    public const string QueryRewriteModel = "llama3.1:8b";
    public const string EmbeddingModel = "text-embedding-3-small";

    public static IServiceCollection AddFraudChatClient(this IServiceCollection services) =>
        services.AddSingleton(sp => Chat("openai", FraudModel, medianMs: 700, inputTokens: 420, outputTokens: 90, failureRate: 0.04, sp));

    public static IServiceCollection AddNotificationChatClient(this IServiceCollection services) =>
        services.AddSingleton(sp => Chat("anthropic", NotificationModel, medianMs: 1500, inputTokens: 260, outputTokens: 140, failureRate: 0.02, sp));

    public static IServiceCollection AddQueryRewriteChatClient(this IServiceCollection services) =>
        services.AddSingleton(sp => Chat("ollama", QueryRewriteModel, medianMs: 320, inputTokens: 60, outputTokens: 14, failureRate: 0.01, sp));

    public static IServiceCollection AddCartEmbeddings(this IServiceCollection services) =>
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            new FakeEmbeddingGenerator("openai", EmbeddingModel)
                .AsBuilder()
                .UseOpenTelemetry(sp.GetService<ILoggerFactory>(), InstrumentationName)
                .Build());

    private static IChatClient Chat(string provider, string model, double medianMs, int inputTokens, int outputTokens, double failureRate, IServiceProvider sp) =>
        new FakeChatClient(provider, model, medianMs, inputTokens, outputTokens, failureRate)
            .AsBuilder()
            .UseOpenTelemetry(sp.GetService<ILoggerFactory>(), InstrumentationName)
            .Build();
}

/// <summary>A model that takes a while, answers with canned text and reports token usage - and now and then fails like a rate-limited provider.</summary>
internal sealed class FakeChatClient(string provider, string model, double medianMs, int inputTokens, int outputTokens, double failureRate) : IChatClient
{
    private readonly ChatClientMetadata _metadata = new(provider, new Uri($"https://api.{provider}.com/"), model);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await Latency.DelayAsync(medianMs, cancellationToken, sigma: 0.45);
        if (Random.Shared.NextDouble() < failureRate)
        {
            throw new HttpRequestException($"{provider} returned 429 Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests);
        }

        // Token counts wobble around their typical size; output grows with how much was asked.
        var input = (int)(inputTokens * (0.7 + Random.Shared.NextDouble() * 0.6));
        var output = (int)(outputTokens * (0.6 + Random.Shared.NextDouble() * 0.8));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Looks consistent with the customer's usual purchases."))
        {
            ModelId = model,
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output },
        };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The demo model only answers non-streaming requests.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata : null;

    public void Dispose()
    {
    }
}

internal sealed class FakeEmbeddingGenerator(string provider, string model) : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly EmbeddingGeneratorMetadata _metadata = new(provider, new Uri($"https://api.{provider}.com/"), model, 8);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await Latency.DelayAsync(70, cancellationToken);
        var inputs = values.ToList();
        var result = new GeneratedEmbeddings<Embedding<float>>(inputs.Select(_ =>
            new Embedding<float>(Enumerable.Range(0, 8).Select(_ => Random.Shared.NextSingle()).ToArray()) { ModelId = model }));
        var tokens = inputs.Sum(v => Math.Max(1, v.Length / 4));
        result.Usage = new UsageDetails { InputTokenCount = tokens, TotalTokenCount = tokens };
        return result;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata : null;

    public void Dispose()
    {
    }
}
