using System.Diagnostics;
using System.Net;
using Confluent.Kafka;
using Microsoft.Extensions.AI;

namespace ExampleApp.Shop;

/// <summary>Charges cards through Stripe, asking fraud-check first for a share of payments.</summary>
public static class PaymentService
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddShopServiceClient("fraud-check");
        builder.AddKafkaProducer();
    }

    public static void Map(WebApplication app) =>
        app.MapPost("/payments/charge", async (
            ChargeRequest request, IHttpClientFactory clients, ExternalApiClient external, IProducer<string, string> kafka,
            ScenarioState scenario, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger(typeof(PaymentService));

            if (scenario.Current.LatencySpike)
            {
                // "Waiting on the card network" - most of the latency-spike scenario's p95 jump.
                await Latency.DelayAsync(1400, ct, sigma: 0.5);
            }

            // Logs the raw card number, the way too many real payment services do - the
            // "Redact card numbers" pipeline rule demo masks it on ingest.
            logger.LogInformation("Charging card {CardNumber} for order {OrderId}: {Amount} {Currency}", request.CardNumber, request.OrderId, request.Amount, request.Currency);

            // Roughly a third of payments get a fraud score first - the structural-query demo's
            // "checkout-api -> payment-service, with or without fraud-check in between".
            if (request.Amount > 150 || Random.Shared.NextDouble() < 0.25)
            {
                using var fraudResponse = await clients.CreateClient("fraud-check").PostAsJsonAsync("/fraud/score", request, ct);
                var score = (await fraudResponse.Content.ReadFromJsonAsync<FraudScore>(ct))?.Score ?? 0;
                if (score > 0.93)
                {
                    logger.LogWarning("Payment for order {OrderId} blocked by fraud-check (score {Score:F2})", request.OrderId, score);
                    return Results.Problem("Blocked by fraud screening", statusCode: StatusCodes.Status402PaymentRequired);
                }
            }

            try
            {
                using var stripe = await external.StripeCreatePaymentIntentAsync(request.Amount, request.Currency, ct);
                switch (stripe.StatusCode)
                {
                    case HttpStatusCode.PaymentRequired:
                        throw new PaymentDeclinedException(request.OrderId, "card_declined");
                    case HttpStatusCode.TooManyRequests:
                        logger.LogWarning("Stripe rate-limited the charge for order {OrderId}", request.OrderId);
                        return Results.Problem("Payment provider busy, retry later", statusCode: StatusCodes.Status503ServiceUnavailable);
                    case >= HttpStatusCode.BadRequest:
                        logger.LogError("Stripe returned {StatusCode} for order {OrderId}", (int)stripe.StatusCode, request.OrderId);
                        return Results.Problem("Payment provider error", statusCode: StatusCodes.Status502BadGateway);
                }
            }
            catch (PaymentDeclinedException ex)
            {
                // Handled, so ASP.NET Core instrumentation won't record it - add the OTel
                // `exception` event by hand so declines still group on the Errors page.
                Activity.Current?.AddException(ex);
                logger.LogWarning(ex, "Card declined for order {OrderId}", request.OrderId);
                return Results.Problem("Card declined", statusCode: StatusCodes.Status402PaymentRequired);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                Activity.Current?.AddException(ex);
                logger.LogError(ex, "Stripe did not answer in time for order {OrderId}", request.OrderId);
                return Results.Problem("Payment provider timed out", statusCode: StatusCodes.Status504GatewayTimeout);
            }

            await kafka.ProduceAsync(KafkaClients.PaymentsCompleted, new Message<string, string>
            {
                Key = request.OrderId.ToString(),
                Value = new OrderEvent(request.OrderId, request.UserId, request.Amount, request.Currency).ToJson(),
            }, ct);

            return Results.Ok(new { request.OrderId, status = "captured" });
        });
}

public sealed record FraudScore(double Score);

public sealed class PaymentDeclinedException(Guid orderId, string declineCode)
    : Exception($"Payment declined by issuer: {declineCode}")
{
    public Guid OrderId { get; } = orderId;
    public string DeclineCode { get; } = declineCode;
}

/// <summary>
/// Scores a payment for fraud risk - a slow-ish model call, and the optional hop in the payment
/// path. A chat model writes the rationale shown to reviewers; when the provider rate-limits it
/// the score still goes out, just without one.
/// </summary>
public static class FraudCheck
{
    public static void Map(WebApplication app) =>
        app.MapPost("/fraud/score", async (ChargeRequest request, IChatClient chat, ILoggerFactory loggers, CancellationToken ct) =>
        {
            await Latency.DelayAsync(180, ct);
            // Mostly low scores, a thin tail of high ones.
            var score = Math.Pow(Random.Shared.NextDouble(), 6);

            try
            {
                await chat.GetResponseAsync(
                    $"Explain in one sentence why a {request.Amount} {request.Currency} card payment (order {request.OrderId}) scored {score:F2} for fraud risk.",
                    new ChatOptions { ModelId = LlmClients.FraudModel, MaxOutputTokens = 120 }, ct);
            }
            catch (HttpRequestException ex)
            {
                loggers.CreateLogger(typeof(FraudCheck)).LogWarning(ex, "Fraud rationale unavailable for order {OrderId}", request.OrderId);
            }

            return Results.Ok(new FraudScore(score));
        });
}
