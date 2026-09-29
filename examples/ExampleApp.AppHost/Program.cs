using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Registers Docker Compose as a deployment target - inert for the default `aspire run`/
// `dotnet run` inner loop, but makes `aspire publish`/`aspire do prepare-compose`/`aspire
// deploy` produce a real docker-compose.yaml for this whole AppHost (Flare included). See
// docs/aspire-hosting.md's "Publishing / deploying via aspire publish" section - this is
// its worked example. AddFlare's WithPublicApiUrl/WithPublicDashboardUrl chain methods
// (not used below) only matter once actually deploying off this machine; leave them
// uncalled for `aspire run`.
builder.AddDockerComposeEnvironment("env");

// enableResourceGraph defaults to false (see its doc comment on AddFlare) - left off here
// too, so this example's default footprint doesn't grow a Docker-socket-proxy sidecar for
// everyone who runs it. Pass `enableResourceGraph: true` to exercise the dashboard's
// Resources page against this AppHost - see docs/aspire-hosting.md.
//
// imageTag: "edge" is explicit and deliberate here, overriding AddFlare's own pinned-stable
// default - this example ProjectReferences Flare's local, unreleased source (see this
// project's .csproj), so it should always validate against main-tip images, not whatever
// stable version the published NuGet package currently pins.
var flare = builder.AddFlare("flare", imageTag: "edge");

// ---------------------------------------------------------------------------------------
// The shop: seven services plus a fake third-party upstream, all one project
// (ExampleApp.Shop) started once per role - see that project's ShopRole.cs for the call
// graph. Real ASP.NET Core/HttpClient/Kafka/Npgsql instrumentation throughout, so
// every page of the Flare dashboard except Kubernetes has live data (Kubernetes needs a
// cluster; use ExampleApp.Seeder's `kubernetes` scenario for that page).
// ---------------------------------------------------------------------------------------

var kafka = builder.AddKafka("kafka");

var shopdb = builder.AddPostgres("postgres").AddDatabase("shopdb");

IResourceBuilder<ProjectResource> AddShopService(string name) =>
    // No launch profile: eight resources share this one project, so each gets its own
    // Aspire-assigned port instead of a launchSettings.json applicationUrl they'd all fight
    // over. Aspire's own OTEL_SERVICE_NAME is the resource name, so that's the service.name.
    //
    // .WithReference(flare) injects ConnectionStrings__flare (Flare.Ingest's OTLP/gRPC
    // endpoint), which Flare.Aspire's builder.AddFlareOtlpExporter("flare") reads. .WaitFor(flare)
    // would NOT actually wait - flare is a lifetime-less grouping node and Aspire skips those as
    // WaitFor targets (see WaitForFlare's doc comment); WaitForFlare waits on the real dashboard
    // resource instead, which transitively depends on everything else.
    builder.AddProject<Projects.ExampleApp_Shop>(name, launchProfileName: null)
        .WithHttpEndpoint()
        .WithHttpHealthCheck("/health")
        .WithEnvironment("Shop__Role", name)
        .WithReference(flare)
        .WaitForFlare(flare);

var fakeUpstream = AddShopService("fake-upstream");

var inventory = AddShopService("inventory-service")
    .WithReference(shopdb).WaitFor(shopdb)
    .WithReference(kafka).WaitFor(kafka);

var fraudCheck = AddShopService("fraud-check");

var payment = AddShopService("payment-service")
    .WithReference(fraudCheck)
    .WithReference(fakeUpstream)
    .WithReference(kafka).WaitFor(kafka);

var order = AddShopService("order-service")
    .WithReference(shopdb).WaitFor(shopdb)
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(fakeUpstream);

var notification = AddShopService("notification-service")
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(fakeUpstream);

var checkout = AddShopService("checkout-api")
    .WithReference(inventory)
    .WithReference(payment)
    .WithReference(order)
    .WithReference(fakeUpstream)
    .WithReference(kafka).WaitFor(kafka)
    // "true" tags checkout.cart.items_added with user.id - thousands of series from one
    // metric, for the Metrics catalog's cardinality view. Off by default; see ShopMetrics.
    .WithEnvironment("Shop__HighCardinalityMetrics", "false");

// storefront references every other shop service, not just the ones it calls: POST
// /scenario on storefront fans the change out to each of them (see ScenarioEndpoints).
var storefront = AddShopService("storefront")
    .WithReference(checkout)
    .WithReference(inventory)
    .WithReference(payment)
    .WithReference(fraudCheck)
    .WithReference(order)
    .WithReference(notification)
    .WithReference(fakeUpstream)
    .WithReference(kafka).WaitFor(kafka)
    .WithEnvironment("Shop__Traffic__RequestsPerSecond", "3")
    // Failure-mode switches in the Aspire dashboard's resource menu - the same endpoints
    // examples/README.md shows with curl.
    .WithHttpCommand("/scenario/latency-spike/on", "Scenario: latency spike", commandName: "scenario-latency-spike")
    .WithHttpCommand("/scenario/partner-outage/on", "Scenario: partner outage (503s)", commandName: "scenario-partner-outage")
    .WithHttpCommand("/scenario/consumer-slowdown/on", "Scenario: consumer slowdown", commandName: "scenario-consumer-slowdown")
    .WithHttpCommand("/scenario/crash-loop/on", "Scenario: crash loop", commandName: "scenario-crash-loop")
    .WithHttpCommand("/scenario/reset", "Scenario: reset all", commandName: "scenario-reset");

// ---------------------------------------------------------------------------------------
// otelcol-contrib: the metrics Flare can't get from the apps themselves - host metrics for
// the Hosts page, and Kafka consumer lag for the Message queues page's Backlog column. Config
// in otelcol.yaml next to this file.
// ---------------------------------------------------------------------------------------
builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.161.0")
    .WithBindMount("otelcol.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    // hostmetrics reads /proc, /sys and the mount table under root_path - without the host's
    // root filesystem mounted there, the filesystem scraper reports nothing. On Docker
    // Desktop "the host" is Docker's Linux VM, not macOS/Windows itself.
    .WithBindMount("/", "/hostfs", isReadOnly: true)
    // resourcedetection's `os` hostname source reads the container's hostname - pin it, so
    // the Hosts page shows one stable host instead of a new container id per restart.
    .WithContainerRuntimeArgs("--hostname", "shop-docker-host")
    // Endpoint references resolve in the container network here (flare-ingest:4317,
    // kafka:9093), not to the host-mapped localhost ports.
    .WithEnvironment("FLARE_OTLP_ENDPOINT", flare.Resource.OtlpGrpcEndpoint)
    .WithEnvironment("KAFKA_BROKERS", ReferenceExpression.Create($"{kafka.Resource.InternalEndpoint.Property(EndpointProperty.HostAndPort)}"))
    .WaitFor(kafka)
    .WaitForFlare(flare);

builder.Build().Run();
