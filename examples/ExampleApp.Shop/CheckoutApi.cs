using System.Net;
using Confluent.Kafka;

namespace ExampleApp.Shop;

/// <summary>
/// Orchestrates a checkout: reserve stock, quote shipping, check partner stock, charge the card,
/// confirm the order, announce it on Kafka. The funnel storefront → checkout-api →
/// payment-service → order-service that the Trace funnels page is built around.
/// </summary>
public static class CheckoutApi
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services
            .AddShopServiceClient("inventory-service")
            .AddShopServiceClient("payment-service")
            .AddShopServiceClient("order-service");
        builder.AddKafkaProducer();
    }

    public static void Map(WebApplication app) =>
        app.MapPost("/checkout", async (
            CheckoutRequest request, IHttpClientFactory clients, ExternalApiClient external, IProducer<string, string> kafka,
            ShopMetrics metrics, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger(typeof(CheckoutApi));
            var orderId = Guid.NewGuid();

            // Body text on purpose: the "Extract user_id" pipeline rule demo pulls user_id=NNN
            // out of exactly this line.
            logger.LogInformation("Checkout started user_id={UserId} items={ItemCount} currency={Currency}", request.UserId, request.Items.Count, request.Currency);
            metrics.RecordItemsAdded(request.Items.Sum(i => i.Quantity), request.UserId, request.Currency);

            var discount = PromoCodes.DiscountFor(request.PromoCode);

            using var reserveResponse = await clients.CreateClient("inventory-service")
                .PostAsJsonAsync("/inventory/reserve", new ReserveRequest(orderId, request.Items), ct);
            if (!reserveResponse.IsSuccessStatusCode)
            {
                logger.LogError("Stock reservation failed for order {OrderId}: inventory-service returned {StatusCode}", orderId, (int)reserveResponse.StatusCode);
                return Results.Problem("Could not reserve stock", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var reservation = (await reserveResponse.Content.ReadFromJsonAsync<ReserveResult>(ct))!;
            var total = Math.Round(reservation.Total * (1 - discount), 2);

            using (var shipping = await external.MapsDistanceAsync(ct))
            {
                if (!shipping.IsSuccessStatusCode)
                {
                    logger.LogWarning("Shipping estimate unavailable ({StatusCode}), using the flat rate", (int)shipping.StatusCode);
                }
            }

            foreach (var partnerSku in reservation.PartnerSkus)
            {
                try
                {
                    using var stock = await external.PartnerStockAsync(partnerSku, ct);
                    if (stock.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        logger.LogWarning("Partner stock check for {PartnerSku} failed with 503, falling back to local stock", partnerSku);
                        continue;
                    }

                    using var _ = await external.PartnerReserveAsync(partnerSku, 1, ct);
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Partner stock check for {PartnerSku} timed out", partnerSku);
                }
            }

            using var chargeResponse = await clients.CreateClient("payment-service")
                .PostAsJsonAsync("/payments/charge", new ChargeRequest(orderId, request.UserId, total, request.Currency, request.CardNumber), ct);
            if (!chargeResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Payment for order {OrderId} failed with {StatusCode}", orderId, (int)chargeResponse.StatusCode);
                return Results.Problem("Payment failed", statusCode: (int)chargeResponse.StatusCode);
            }

            using var confirmResponse = await clients.CreateClient("order-service")
                .PostAsJsonAsync("/orders/confirm", new ConfirmOrderRequest(orderId, request.UserId, total, request.Currency, request.Items, request.Email), ct);
            if (!confirmResponse.IsSuccessStatusCode)
            {
                logger.LogError("Order {OrderId} was charged but not confirmed: order-service returned {StatusCode}", orderId, (int)confirmResponse.StatusCode);
                return Results.Problem("Order confirmation failed", statusCode: StatusCodes.Status502BadGateway);
            }

            await kafka.ProduceAsync(KafkaClients.OrdersCreated, new Message<string, string>
            {
                Key = orderId.ToString(),
                Value = new OrderEvent(orderId, request.UserId, total, request.Currency, request.Email, request.Phone).ToJson(),
            }, ct);

            logger.LogInformation("Checkout completed for order {OrderId}: {Total} {Currency}", orderId, total, request.Currency);
            return Results.Created($"/orders/{orderId}", new { orderId, total });
        });
}

/// <summary>Promo-code lookup, with a real bug in it for the Errors page to find.</summary>
public static class PromoCodes
{
    private sealed record Promo(decimal Percent, DateOnly ExpiresOn);

    // SPRING-24 was deleted from the table when it expired, but storefront still sends it.
    private static readonly Dictionary<string, Promo> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELCOME10"] = new(0.10m, new DateOnly(2099, 1, 1)),
    };

    public static decimal DiscountFor(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return 0;
        }

        // The bug: no null check for a code that's no longer in the table - a
        // NullReferenceException that escapes the request (a 500, and an `exception` event on
        // checkout-api's server span).
        var promo = Codes.GetValueOrDefault(code);
        return promo!.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow) ? 0 : promo.Percent;
    }
}
