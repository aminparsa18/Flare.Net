using Confluent.Kafka;
using Confluent.Kafka.Admin;
using OpenTelemetry.Instrumentation.ConfluentKafka;

namespace ExampleApp.Shop;

/// <summary>
/// Confluent.Kafka producers/consumers with OpenTelemetry.Instrumentation.ConfluentKafka's
/// tracing and metrics turned on - <c>{topic} publish</c> spans on the producer, <c>{topic}
/// receive</c> + <c>{topic} process</c> spans on the consumer (the process span parented to the
/// publish span via the propagated headers), which is what Flare's Message queues page reads.
/// </summary>
public static class KafkaClients
{
    /// <summary>The instrumentation's ActivitySource and Meter name - see Program.cs for why it's subscribed by hand.</summary>
    public const string InstrumentationName = "OpenTelemetry.Instrumentation.ConfluentKafka";

    public const string OrdersCreated = "orders.created";
    public const string PaymentsCompleted = "payments.completed";
    public const string InventoryReserved = "inventory.reserved";
    public const string ClickstreamEvents = "clickstream.events";

    private static readonly string[] Topics = [OrdersCreated, PaymentsCompleted, InventoryReserved, ClickstreamEvents];

    public static string BootstrapServers(IConfiguration configuration) =>
        configuration.GetConnectionString("kafka")
        ?? throw new InvalidOperationException("No 'kafka' connection string - reference Kafka from the AppHost (.WithReference(kafka)).");

    /// <summary>Registers a shared instrumented producer, and creates the shop's topics (3 partitions each) before the host starts.</summary>
    public static void AddKafkaProducer(this WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<KafkaTopicsInitializer>();
        builder.Services.AddSingleton(sp =>
        {
            // A real ProducerConfig, not a KeyValuePair list - the instrumented builder casts
            // its config to ProducerConfig and throws InvalidCastException otherwise.
            var config = new ProducerConfig
            {
                BootstrapServers = BootstrapServers(sp.GetRequiredService<IConfiguration>()),
                ClientId = sp.GetRequiredService<ShopRoleInfo>().Name,
                LingerMs = 5,
                // librdkafka's default is 5 minutes - an HTTP request awaiting a publish
                // shouldn't hang that long if the broker is gone.
                MessageTimeoutMs = 5000,
            };
            return new ProducerBuilder<string, string>(config)
                .AsInstrumentedProducerBuilder(new ConfluentKafkaInstrumentedProducerBuilderOptions { EnableTraces = true, EnableMetrics = true })
                .Build();
        });
    }

    private sealed class KafkaTopicsInitializer(IConfiguration configuration, ILogger<KafkaTopicsInitializer> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers(configuration) }).Build();
            try
            {
                await admin.CreateTopicsAsync(Topics.Select(t => new TopicSpecification { Name = t, NumPartitions = 3, ReplicationFactor = 1 }));
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
            {
                // Another shop service got there first.
            }

            logger.LogInformation("Kafka topics ready: {Topics}", string.Join(", ", Topics));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>
/// One consumer group reading one topic, each message handled inside the instrumentation's
/// <c>process</c> span (<see cref="OpenTelemetryConsumeResultExtensions.ConsumeAndProcessMessageAsync{TKey, TValue}(IConsumer{TKey, TValue}, OpenTelemetryConsumeAndProcessMessageHandler{TKey, TValue}, CancellationToken)"/>),
/// so anything the handler does - SQL, HTTP - is a child of it.
/// </summary>
public abstract class KafkaConsumerWorker(IConfiguration configuration, ILogger logger, string topic, string groupId) : BackgroundService
{
    // Confluent's Consume() blocks its thread - keep it off the thread pool.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => ConsumeLoopAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    protected abstract ValueTask HandleAsync(ConsumeResult<string, string> message, CancellationToken cancellationToken);

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = KafkaClients.BootstrapServers(configuration),
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
        };
        using var consumer = new ConsumerBuilder<string, string>(config)
            .AsInstrumentedConsumerBuilder(new ConfluentKafkaInstrumentedConsumerBuilderOptions { EnableTraces = true, EnableMetrics = true })
            .Build();
        consumer.Subscribe(topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumer.ConsumeAndProcessMessageAsync((message, _, ct) => HandleAsync(message, ct), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                logger.LogWarning(ex, "Kafka consume failed for {Topic} ({GroupId})", topic, groupId);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (Exception ex)
            {
                // The handler's own failure - already recorded on the process span; log and move on
                // (at-most-once for the demo, auto-commit already advanced past it).
                logger.LogError(ex, "Failed to process {Topic} message for group {GroupId}", topic, groupId);
            }
        }

        consumer.Close();
    }
}
