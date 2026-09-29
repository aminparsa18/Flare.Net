using System.Text.Json;
using Confluent.Kafka;
using Npgsql;

namespace ExampleApp.Shop;

/// <summary>
/// Owns the <c>orders</c> table: confirms orders over HTTP, then keeps them in sync from three
/// Kafka consumer groups.
/// </summary>
public static class OrderService
{
    // Recently confirmed ids - the duplicate-confirmation bug below replays one of these.
    private static readonly Guid[] Recent = new Guid[16];

    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.AddShopDatabase();
        builder.Services.AddHostedService<OrderProcessor>();
        builder.Services.AddHostedService<PaymentReconciler>();
        builder.Services.AddHostedService<InventorySync>();
    }

    public static void Map(WebApplication app) =>
        app.MapPost("/orders/confirm", async (
            ConfirmOrderRequest request, NpgsqlDataSource db, ExternalApiClient external,
            ShopMetrics metrics, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger(typeof(OrderService));

            // The bug: a retried confirmation (roughly 1 in 70) re-inserts an order id that's
            // already there. Npgsql throws a real PostgresException (23505) that escapes the
            // request - a 500, an error-status INSERT span, and an `exception` event.
            var orderId = request.OrderId;
            var replay = Recent[Random.Shared.Next(Recent.Length)];
            if (replay != Guid.Empty && Random.Shared.NextDouble() < 0.015)
            {
                orderId = replay;
            }

            await using (var insert = db.CreateCommand("INSERT INTO orders (id, user_id, total, currency, status) VALUES ($1, $2, $3, $4, 'confirmed')"))
            {
                insert.Parameters.Add(new() { Value = orderId });
                insert.Parameters.Add(new() { Value = request.UserId });
                insert.Parameters.Add(new() { Value = request.Total });
                insert.Parameters.Add(new() { Value = request.Currency });
                await insert.ExecuteNonQueryAsync(ct);
            }

            Recent[Random.Shared.Next(Recent.Length)] = orderId;
            metrics.RecordOrderPlaced(request.Total, request.Currency);

            // The whole log body is one JSON document - what the "Parse JSON bodies" pipeline
            // rule demo flattens into attributes, and what the Logs page's JSON body filters query.
            logger.LogInformation("{OrderJson}", JsonSerializer.Serialize(new
            {
                @event = "order.confirmed",
                order_id = orderId,
                user_id = request.UserId,
                total = request.Total,
                currency = request.Currency,
                items = request.Items.Select(i => new { sku = i.Sku, qty = i.Quantity }),
                shipping = new { country = Random.Shared.GetItems<string>(["DE", "FR", "US", "GB", "NL"], 1)[0], method = Random.Shared.NextDouble() < 0.3 ? "express" : "standard" },
            }));

            if (request.Total > 600)
            {
                using var slack = await external.SlackPostAsync($"Big order {orderId}: {request.Total} {request.Currency}", ct);
            }

            return Results.Ok(new { orderId, status = "confirmed" });
        });

    private static async Task SetStatusAsync(NpgsqlDataSource db, Guid orderId, string status, CancellationToken ct)
    {
        await using var update = db.CreateCommand("UPDATE orders SET status = $2 WHERE id = $1");
        update.Parameters.Add(new() { Value = orderId });
        update.Parameters.Add(new() { Value = status });
        await update.ExecuteNonQueryAsync(ct);
    }

    private sealed class OrderProcessor(IConfiguration configuration, ILogger<OrderProcessor> logger, NpgsqlDataSource db)
        : KafkaConsumerWorker(configuration, logger, KafkaClients.OrdersCreated, "order-processor")
    {
        protected override async ValueTask HandleAsync(ConsumeResult<string, string> message, CancellationToken ct) =>
            await SetStatusAsync(db, OrderEvent.FromJson(message.Message.Value).OrderId, "processing", ct);
    }

    /// <summary>Matches captured payments against orders - the consumer group the consumer-slowdown scenario throttles.</summary>
    private sealed class PaymentReconciler(IConfiguration configuration, ILogger<PaymentReconciler> logger, NpgsqlDataSource db, ScenarioState scenario)
        : KafkaConsumerWorker(configuration, logger, KafkaClients.PaymentsCompleted, "payment-reconciler")
    {
        protected override async ValueTask HandleAsync(ConsumeResult<string, string> message, CancellationToken ct)
        {
            var payment = OrderEvent.FromJson(message.Message.Value);
            await using (var select = db.CreateCommand("SELECT total FROM orders WHERE id = $1"))
            {
                select.Parameters.Add(new() { Value = payment.OrderId });
                await select.ExecuteScalarAsync(ct);
            }

            // Slower than payments arrive while the scenario is on, so the group's lag climbs.
            await Latency.DelayAsync(scenario.Current.ConsumerSlowdown ? 2500 : 40, ct);
            await SetStatusAsync(db, payment.OrderId, "paid", ct);
        }
    }

    private sealed class InventorySync(IConfiguration configuration, ILogger<InventorySync> logger)
        : KafkaConsumerWorker(configuration, logger, KafkaClients.InventoryReserved, "inventory-sync")
    {
        protected override ValueTask HandleAsync(ConsumeResult<string, string> message, CancellationToken ct) =>
            new(Latency.DelayAsync(8, ct));
    }
}
