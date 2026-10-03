using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Kubernetes is the deployment target for the standing demo (.github/workflows/deploy-demo.yml:
// `aspire deploy` against a k3s box). Everything Kubernetes-specific is gated on IsPublishMode, so
// `aspire run`/`dotnet run` keeps behaving exactly as before. See docs/reference/aspire-hosting.md's
// Kubernetes section for why persistent storage, the registry and the public URLs are each required.
#pragma warning disable ASPIRECOMPUTE002, ASPIRECOMPUTE003 // AddPersistentVolume / AddContainerRegistry are preview APIs
var publishing = builder.ExecutionContext.IsPublishMode;

// No in-cluster Aspire dashboard: it costs RAM on a small box and would be one more public surface.
var k8s = builder.AddKubernetesEnvironment("k8s").WithDashboard(false);

// Parameters are only declared when publishing: declared-but-unused parameters would still be
// prompted for under `aspire run`.
IResourceBuilder<ParameterResource>? publicApiUrl = null, publicDashboardUrl = null;
if (publishing)
{
    // Images for the shop services (and the generated ClickHouse-init image) are pushed here.
    var registryEndpoint = builder.AddParameter("registry-endpoint");
    var registryRepository = builder.AddParameter("registry-repository");
    k8s.WithContainerRegistry(builder.AddContainerRegistry("registry", registryEndpoint, registryRepository));

    // Browser-facing URLs for the dashboard and the API. Left unset the dashboard would point at
    // in-cluster Service DNS names. With no domain yet, the workflow passes <server-ip>.sslip.io
    // (wildcard DNS: any name under it resolves to the IP).
    publicApiUrl = builder.AddParameter("public-api-url");
    publicDashboardUrl = builder.AddParameter("public-dashboard-url");
}

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

if (publishing)
{
    flare.WithPublicApiUrl(publicApiUrl!)
        .WithPublicDashboardUrl(publicDashboardUrl!)
        // k3s ships the local-path storage class; ReadWriteOnce is all it offers (single node, so
        // the shared identity volume is fine - see the reference doc's caveat about multi-node).
        .WithPersistentStorage(
            clickHouseVolume: k8s.AddPersistentVolume("flare-clickhouse-data")
                .WithStorageClass("local-path").WithCapacity("20Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            redisVolume: k8s.AddPersistentVolume("flare-redis-data")
                .WithStorageClass("local-path").WithCapacity("2Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            identityVolume: k8s.AddPersistentVolume("flare-identity-data")
                .WithStorageClass("local-path").WithCapacity("1Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce));

    // Ingress (k3s ships Traefik). AddFlare doesn't hand back its dashboard/api builders, so look
    // them up by the names it gives them. The hostnames are read from configuration (the same
    // Parameters__* env vars) rather than declared as parameters: in this Aspire.Hosting.Kubernetes
    // preview WithHostname(parameter) silently emits no `host:` rule, and so does WithHostname(string)
    // combined with the host-less WithPath; only the host-scoped WithPath(host, ...) overload does.
    // An ingress with no host matches every host, so the two would collide on "/".
    string RequiredSetting(string key) =>
        builder.Configuration[$"Parameters:{key}"] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Set Parameters__{key} (the {key.Replace('_', ' ')} the ingress should match).");
    var dashboardHost = RequiredSetting("dashboard_host");
    var apiHost = RequiredSetting("api_host");

    // The ingress also requires the routed endpoint to be marked external.
    EndpointReference FlareEndpoint(string resourceName)
    {
        var resource = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>().Single(r => r.Name == resourceName));
        resource.WithExternalHttpEndpoints();
        return resource.GetEndpoint("http");
    }

    k8s.AddIngress("flare-dashboard-ingress").WithIngressClass("traefik")
        .WithPath(dashboardHost, "/", FlareEndpoint("flare-dashboard"));
    k8s.AddIngress("flare-api-ingress").WithIngressClass("traefik")
        .WithPath(apiHost, "/", FlareEndpoint("flare-api"));
}

// ---------------------------------------------------------------------------------------
// The shop: seven services plus a fake third-party upstream, all one project
// (ExampleApp.Shop) started once per role - see that project's ShopRole.cs for the call
// graph. Real ASP.NET Core/HttpClient/Kafka/Npgsql instrumentation throughout, so
// every page of the Flare dashboard except Kubernetes has live data (Kubernetes needs a
// cluster; use ExampleApp.Seeder's `kubernetes` scenario for that page).
// ---------------------------------------------------------------------------------------

var kafka = builder.AddKafka("kafka");

var postgres = builder.AddPostgres("postgres");
// AddDatabase creates shopdb only under `aspire run`; the Kubernetes chart gets a bare Postgres
// with just the default `postgres` database, so inventory/order-service crash on connect. The
// official image creates the database named by POSTGRES_DB on first init. (The shop creates its
// own tables on startup - see ShopDatabase.cs.)
if (publishing)
    postgres.WithEnvironment("POSTGRES_DB", "shopdb");
var shopdb = postgres.AddDatabase("shopdb");

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
    // The standing demo runs on a small box, so it gets a third of the local traffic.
    .WithEnvironment("Shop__Traffic__RequestsPerSecond", publishing ? "1" : "3")
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
if (publishing)
{
    // The Kubernetes publisher rejects bind mounts, so on the cluster the config is baked into a
    // tiny image Aspire builds and pushes (Dockerfile.otelcol). That variant has no hostmetrics
    // receiver: it needs the node's root filesystem, which isn't expressible here, so the Hosts
    // page stays empty on the standing demo (use ExampleApp.Seeder's `hosts` scenario for it).
    builder.AddDockerfile("otel-collector", ".", "Dockerfile.otelcol")
        .WithEnvironment("FLARE_OTLP_ENDPOINT", flare.Resource.OtlpGrpcEndpoint)
        .WithEnvironment("KAFKA_BROKERS", ReferenceExpression.Create($"{kafka.Resource.InternalEndpoint.Property(EndpointProperty.HostAndPort)}"))
        .WaitFor(kafka)
        .WaitForFlare(flare);
}
else
{
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

}

builder.Build().Run();
