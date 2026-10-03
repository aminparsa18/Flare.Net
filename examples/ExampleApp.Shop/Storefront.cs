using System.Diagnostics;
using System.Net.Http.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using OpenTelemetry;

namespace ExampleApp.Shop;

/// <summary>The shop's front door: product pages, the cart, search, and checkout (forwarded to checkout-api).</summary>
public static class Storefront
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddShopServiceClient("checkout-api").AddShopServiceClient("inventory-service");
        builder.AddKafkaProducer();
        builder.Services.AddCartEmbeddings();
        builder.Services.AddQueryRewriteChatClient();
        builder.Services.AddHostedService<TrafficWorker>();
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/products/{sku:int}", async (int sku, IHttpClientFactory clients, IProducer<string, string> kafka, CancellationToken ct) =>
        {
            await TrackAsync(kafka, "product_view", ct);
            using var response = await clients.CreateClient("inventory-service").GetAsync($"/inventory/{sku}", ct);
            return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
        });

        app.MapGet("/cart", async (IHttpClientFactory clients, ExternalApiClient external, IProducer<string, string> kafka, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger(typeof(Storefront));
            await TrackAsync(kafka, "cart_view", ct);
            var inventory = clients.CreateClient("inventory-service");
            for (var i = 0; i < Random.Shared.Next(1, 4); i++)
            {
                using var _ = await inventory.GetAsync($"/inventory/{Random.Shared.Next(1, ShopDatabase.ProductCount + 1)}", ct);
            }

            if (Random.Shared.NextDouble() < 0.3)
            {
                // Delivery estimate for the cart page's address box.
                using var geocode = await external.MapsGeocodeAsync("Alexanderplatz 1, Berlin", ct);
                if (!geocode.IsSuccessStatusCode)
                {
                    logger.LogWarning("Geocoding failed with {StatusCode}, showing the cart without a delivery estimate", (int)geocode.StatusCode);
                }
            }

            return Results.Ok(new { items = Random.Shared.Next(0, 5) });
        });

        app.MapGet("/search", async (string q, IHttpClientFactory clients, IProducer<string, string> kafka, IEmbeddingGenerator<string, Embedding<float>> embeddings, IChatClient chat, CancellationToken ct) =>
        {
            await TrackAsync(kafka, "search", ct);
            // A small self-hosted model tidies the query first (the fake's canned answer is discarded); a failure doesn't block search.
            try
            {
                await chat.GetResponseAsync($"Fix typos in this shop search query: {q}", new ChatOptions { ModelId = LlmClients.QueryRewriteModel, MaxOutputTokens = 32 }, ct);
            }
            catch (HttpRequestException)
            {
            }
            // Embeds the query for the semantic half of search.
            await embeddings.GenerateAsync([q], new EmbeddingGenerationOptions { ModelId = LlmClients.EmbeddingModel }, ct);
            using var response = await clients.CreateClient("inventory-service").GetAsync($"/inventory/search?q={Uri.EscapeDataString(q)}", ct);
            return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
        });

        app.MapPost("/checkout", async (CheckoutRequest request, IHttpClientFactory clients, IProducer<string, string> kafka, CancellationToken ct) =>
        {
            await TrackAsync(kafka, "checkout", ct);
            using var response = await clients.CreateClient("checkout-api").PostAsJsonAsync("/checkout", request, ct);
            return Results.Content(await response.Content.ReadAsStringAsync(ct), "application/json", statusCode: (int)response.StatusCode);
        });
    }

    private static Task TrackAsync(IProducer<string, string> kafka, string eventType, CancellationToken ct) =>
        kafka.ProduceAsync(KafkaClients.ClickstreamEvents, new Message<string, string>
        {
            Key = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            Value = $$"""{"type":"{{eventType}}","at":"{{DateTimeOffset.UtcNow:O}}"}""",
        }, ct);
}

/// <summary>
/// Simulated shoppers: a steady mix of product views, cart views, searches and checkouts
/// against storefront's own endpoints (<c>Shop:Traffic:RequestsPerSecond</c>, default 3).
/// </summary>
/// <remarks>
/// A real shopper's browser isn't instrumented, so storefront's server span should be each
/// trace's root. These calls therefore run inside <see cref="SuppressInstrumentationScope"/>
/// (no HttpClient span is created for them) through a handler that injects no
/// <c>traceparent</c> header.
/// </remarks>
public sealed class TrafficWorker(IServer server, IHostApplicationLifetime lifetime, IConfiguration configuration, ILogger<TrafficWorker> logger) : BackgroundService
{
    private static readonly string[] Currencies = ["USD", "USD", "USD", "USD", "USD", "USD", "USD", "EUR", "EUR", "GBP"];

    // Stripe's documented test card numbers - the payment-service log line that echoes them is
    // what the "Redact card numbers" pipeline rule demo masks.
    private static readonly string[] TestCards = ["4242424242424242", "5555555555554444", "4000056655665556", "378282246310005"];

    private static readonly string[] SearchTerms = ["headphones", "usb-c cable", "desk lamp", "running shoes", "coffee grinder", "notebook"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForStartAsync(stoppingToken);
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient(new SocketsHttpHandler
        {
            ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator(),
        })
        { BaseAddress = new Uri(address.Replace("+", "localhost").Replace("*", "localhost").Replace("[::]", "localhost")) };

        var perSecond = Math.Max(0.1, configuration.GetValue("Shop:Traffic:RequestsPerSecond", 3.0));
        using var inFlight = new SemaphoreSlim(32);
        logger.LogInformation("Simulating shoppers at {RequestsPerSecond} requests/s against {Address}", perSecond, client.BaseAddress);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(-Math.Log(1 - Random.Shared.NextDouble()) / perSecond), stoppingToken);
            if (!await inFlight.WaitAsync(0, stoppingToken))
            {
                continue; // the shop is too slow to keep up - shed load rather than pile up
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var _ = SuppressInstrumentationScope.Begin();
                    using var response = await SendOneAsync(client, stoppingToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Storefront itself is restarting or the request timed out - nothing to record client-side.
                }
                finally
                {
                    inFlight.Release();
                }
            }, stoppingToken);
        }
    }

    private static Task<HttpResponseMessage> SendOneAsync(HttpClient client, CancellationToken ct)
    {
        var roll = Random.Shared.NextDouble();
        if (roll < 0.45)
        {
            return client.GetAsync($"/products/{Random.Shared.Next(1, ShopDatabase.ProductCount + 1)}", ct);
        }

        if (roll < 0.7)
        {
            return client.GetAsync("/cart", ct);
        }

        if (roll < 0.8)
        {
            return client.GetAsync($"/search?q={Uri.EscapeDataString(Random.Shared.GetItems(SearchTerms, 1)[0])}", ct);
        }

        var userId = Random.Shared.Next(1, 20_000);
        var items = Enumerable.Range(0, Random.Shared.Next(1, 5))
            .Select(_ => new CartLine(Random.Shared.Next(1, ShopDatabase.ProductCount + 1), Random.Shared.Next(1, 4)))
            .ToList();
        var promo = Random.Shared.NextDouble() switch
        {
            < 0.12 => "WELCOME10",
            < 0.15 => "SPRING-24", // expired - trips checkout-api's promo-code bug
            _ => null,
        };
        var request = new CheckoutRequest(
            userId, items, Random.Shared.GetItems(Currencies, 1)[0], Random.Shared.GetItems(TestCards, 1)[0],
            $"shopper{userId}@example.com", $"+1555{Random.Shared.Next(1_000_000, 9_999_999)}", promo);
        return client.PostAsJsonAsync("/checkout", request, ct);
    }

    private Task WaitForStartAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        return started.Task.WaitAsync(stoppingToken);
    }
}
