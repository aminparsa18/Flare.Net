using Confluent.Kafka;

namespace ExampleApp.Shop;

/// <summary>
/// Tells shoppers about their orders. No HTTP API - one Kafka consumer group on
/// <c>orders.created</c> that texts the shopper through Twilio and emails the invoice through
/// SendGrid.
/// </summary>
public static class NotificationService
{
    public static void AddServices(WebApplicationBuilder builder) =>
        builder.Services.AddHostedService<OrderNotifier>();

    /// <summary>The second consumer group the consumer-slowdown scenario throttles (invoice PDF rendering).</summary>
    private sealed class OrderNotifier(IConfiguration configuration, ILogger<OrderNotifier> logger, ExternalApiClient external, ScenarioState scenario)
        : KafkaConsumerWorker(configuration, logger, KafkaClients.OrdersCreated, "order-notifier")
    {
        protected override async ValueTask HandleAsync(ConsumeResult<string, string> message, CancellationToken ct)
        {
            var order = OrderEvent.FromJson(message.Message.Value);

            using (var sms = await external.TwilioSendSmsAsync(order.Phone ?? "+15005550009", $"Order {order.OrderId} confirmed: {order.Total} {order.Currency}", ct))
            {
                if (!sms.IsSuccessStatusCode)
                {
                    logger.LogWarning("SMS for order {OrderId} rejected by Twilio with {StatusCode}", order.OrderId, (int)sms.StatusCode);
                }
            }

            // PDF rendering. Slower than orders arrive while the scenario is on, so the group's lag climbs.
            await Latency.DelayAsync(scenario.Current.ConsumerSlowdown ? 3000 : 120, ct);

            using var email = await external.SendGridSendAsync(order.Email ?? "unknown@example.com", $"Your invoice for order {order.OrderId}", ct);
            logger.LogInformation("Invoice for order {OrderId} emailed ({StatusCode})", order.OrderId, (int)email.StatusCode);
        }
    }
}
