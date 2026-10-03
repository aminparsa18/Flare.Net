using ExampleApp.Shop;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// One project, many services: ExampleApp.AppHost starts this project once per shop service
// (storefront, checkout-api, payment-service, ...) and tells each copy which one it is via
// Shop__Role. Every copy is its own process with its own OTEL_SERVICE_NAME (Aspire sets it
// from the resource name), so trace context crosses real process boundaries over real HTTP
// and Kafka - nothing here builds a span by hand. One project instead of seven keeps the
// example's shared plumbing (ServiceDefaults, exporters, the fake-upstream redirect) in
// one place; see ShopRole for what each role serves.
var role = ShopRoles.Parse(builder.Configuration["Shop:Role"]);

// Generic Flare.ServiceDefaults (OTel instrumentation, health checks, service discovery), then
// the one Flare-specific line: Flare.Aspire's named OTLP exporter, reading the
// ConnectionStrings__flare the AppHost's .WithReference(flare) injects.
builder.AddServiceDefaults();
builder.AddFlareOtlpExporter("flare");

// ASP.NET Core instrumentation only records an `exception` span event for an exception that
// escapes the pipeline when asked to - the Errors page groups those events, and the shop's
// deliberate bugs (see CheckoutApi/OrderService) are exactly such unhandled exceptions.
builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options => options.RecordException = true);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        // Confluent.Kafka's instrumentation is wired per producer/consumer builder (see
        // KafkaClients) rather than via AddKafka*Instrumentation<TKey, TValue>(), which
        // resolves a single builder per key/value type from DI - this app has several
        // consumer groups. Builders made that way need their ActivitySource/Meter
        // subscribed by hand, per that package's README.
        .AddSource(KafkaClients.InstrumentationName)
        // Npgsql 10 ships its own ActivitySource - no instrumentation package, just
        // subscribe to it.
        .AddSource("Npgsql")
        // Microsoft.Extensions.AI's UseOpenTelemetry() decorator, named in LlmClients.
        .AddSource(LlmClients.InstrumentationName)
        .AddProcessor(sp => new PeerServiceProcessor(sp.GetRequiredService<IConfiguration>())))
    .WithMetrics(metrics => metrics
        .AddMeter(KafkaClients.InstrumentationName)
        .AddMeter("Npgsql")
        .AddMeter(LlmClients.InstrumentationName)
        .AddMeter(ShopMetrics.MeterName));

builder.Services.AddSingleton(new ShopRoleInfo(role));
builder.Services.AddSingleton<ScenarioState>();
builder.Services.AddSingleton<ShopMetrics>();
builder.Services.AddExternalApiClient();

switch (role)
{
    case ShopRole.Storefront:
        Storefront.AddServices(builder);
        break;
    case ShopRole.CheckoutApi:
        CheckoutApi.AddServices(builder);
        break;
    case ShopRole.PaymentService:
        PaymentService.AddServices(builder);
        break;
    case ShopRole.FraudCheck:
        builder.Services.AddFraudChatClient();
        break;
    case ShopRole.OrderService:
        OrderService.AddServices(builder);
        break;
    case ShopRole.NotificationService:
        NotificationService.AddServices(builder);
        break;
    case ShopRole.InventoryService:
        InventoryService.AddServices(builder);
        break;
}

var app = builder.Build();

app.MapDefaultEndpoints(); // /health, /alive
app.MapScenarioEndpoints(role);

switch (role)
{
    case ShopRole.Storefront:
        Storefront.Map(app);
        break;
    case ShopRole.CheckoutApi:
        CheckoutApi.Map(app);
        break;
    case ShopRole.PaymentService:
        PaymentService.Map(app);
        break;
    case ShopRole.FraudCheck:
        FraudCheck.Map(app);
        break;
    case ShopRole.OrderService:
        OrderService.Map(app);
        break;
    case ShopRole.NotificationService:
        break; // Kafka consumer only - see NotificationService.AddServices
    case ShopRole.InventoryService:
        InventoryService.Map(app);
        break;
    case ShopRole.FakeUpstream:
        FakeUpstream.Map(app);
        break;
}

app.Run();
