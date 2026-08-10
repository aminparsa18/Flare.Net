using Aspire.Hosting.ApplicationModel;

// Put extensions in the Aspire.Hosting namespace to ease discovery - referencing the
// Aspire.Hosting package automatically adds this namespace (same convention Aspire's own
// "Create custom hosting integrations" doc uses).
namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding a <see href="https://github.com/aminparsa18/Flare.Net">Flare</see>
/// log-dashboard stack to a .NET Aspire application model.
/// </summary>
public static class FlareResourceBuilderExtensions
{
    /// <summary>
    /// Adds the Flare stack to the application: ClickHouse (log storage), Redis (the batched
    /// insert buffer), the OTLP ingest receiver, the query API, and the dashboard SPA - wrapping
    /// Flare's published Docker Hub images. Mirrors the resource graph Flare's own
    /// <c>Flare.AppHost/Program.cs</c> wires up locally, swapping <c>AddProject</c> for
    /// <c>AddContainer</c>.
    /// </summary>
    /// <remarks>
    /// Only the dashboard shows up in the Aspire dashboard's resource list by default - the
    /// composite <see cref="FlareResource"/> and its four backing resources (ClickHouse, its
    /// database, Redis, and the ingest/api containers) are marked hidden, since they're
    /// implementation details a consumer adding Flare to their own AppHost doesn't need to see.
    /// They're still fully orchestrated (health-checked, waited-on, etc.) - just not shown by
    /// default. Toggle "Show hidden resources" in the dashboard, or use
    /// <c>aspire describe --include-hidden</c> / <c>aspire ps --include-hidden</c>, to see them.
    /// <para>
    /// The returned <see cref="FlareResource"/> is <em>not</em> itself something you can
    /// <c>.WaitFor()</c> - it implements <see cref="IResourceWithoutLifetime"/> (a pure grouping
    /// node with no process of its own), and Aspire's own orchestrator unconditionally skips
    /// <see cref="IResourceWithoutLifetime"/> targets in <c>WaitFor</c>'s dependency wait, no
    /// matter what health checks are attached to them. Use
    /// <see cref="WaitForFlare{TDestination}"/> instead to block a consuming resource until the
    /// whole Flare stack is actually ready.
    /// </para>
    /// <para>
    /// The returned <see cref="FlareResource"/> exposes Flare.Ingest's OTLP endpoints via
    /// <see cref="FlareResource.OtlpGrpcEndpoint"/>/<see cref="FlareResource.OtlpHttpEndpoint"/>
    /// and a <c>ConnectionStringExpression</c> usable with <c>.WithReference(flare)</c>. Point a
    /// consuming resource's OTLP exporter at ingest with <see cref="WithOtlpEndpoint{TDestination}"/> instead
    /// of hand-writing <c>.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")</c>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the Flare resource group.</param>
    /// <param name="imageTag">
    /// The tag to pull for all three Flare images. Defaults to <c>"edge"</c> - Flare is
    /// pre-alpha with no stable release yet, and CI (<c>.github/workflows/docker-publish.yml</c>
    /// in Flare's own repo) only publishes <c>edge</c> until a first <c>v*.*.*</c> tag lands.
    /// </param>
    /// <param name="ingestGrpcPort">
    /// Optional host port for the OTLP gRPC endpoint. Left unset, Aspire assigns the
    /// conventional 4317. Always unproxied so external OTLP clients (your own app's logger) can
    /// point at it directly, same as <c>Flare.AppHost/Program.cs</c>.
    /// </param>
    /// <param name="ingestHttpPort">
    /// Optional host port for the OTLP HTTP endpoint. Left unset, Aspire assigns the
    /// conventional 4318. Always unproxied, same reasoning as <paramref name="ingestGrpcPort"/>.
    /// </param>
    /// <param name="apiPort">Optional host port for Flare's query API. A normal proxied Aspire HTTP endpoint.</param>
    /// <param name="dashboardPort">Optional host port for the dashboard SPA. A normal proxied Aspire HTTP endpoint.</param>
    /// <param name="ingestImage">
    /// Override for the ingest image name (registry/repo, no tag - <paramref name="imageTag"/> still supplies
    /// the tag). Defaults to <see cref="FlareContainerImageTags.IngestImage"/> (Docker Hub). Local-dev escape
    /// hatch for pointing at images built with <c>docker compose build</c> instead of Docker Hub, e.g.
    /// <c>"flarenet-ingest"</c> with <c>imageTag: "latest"</c> - Docker won't re-pull a mutable tag like
    /// <c>edge</c> that's already cached locally, so this is how to force local source into an AppHost run
    /// without waiting on a fresh Docker Hub publish.
    /// </param>
    /// <param name="apiImage">Same override as <paramref name="ingestImage"/>, for the api image.</param>
    /// <param name="dashboardImage">Same override as <paramref name="ingestImage"/>, for the dashboard image.</param>
    /// <param name="apiKey">
    /// Optional ingest API key parameter (pass a <c>secret: true</c> <c>AddParameter</c>
    /// result) - when set, <c>ingest</c> gets <c>Auth__IngestKeyRequired=true</c> and
    /// <c>Auth__StaticIngestApiKey</c> set to this value, so any OTLP exporter pointed
    /// at this Flare instance must present it
    /// (see <c>Flare.Identity.Auth.IngestAuthOptions.StaticIngestApiKey</c>'s remarks for
    /// why this is a separate, config-driven mechanism from the dashboard's "create a
    /// key" flow). Left unset (the default), ingest stays anonymous, matching today's
    /// Flare.Ingest default. A consuming app's own <c>AddFlareOtlpExporter</c> call
    /// (from the <c>Aspire.Flare</c> package) needs the same raw value passed to its own
    /// <c>configureSettings: s =&gt; s.ApiKey = ...</c> delegate - there's no automatic
    /// flow-through from this parameter yet, see <c>FlareSettings.ApiKey</c>'s remarks.
    /// </param>
    /// <returns>An <see cref="IResourceBuilder{FlareResource}"/> for the composite Flare resource.</returns>
    /// <exception cref="ArgumentException"><paramref name="imageTag"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> AddFlare(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "flare",
        string imageTag = "edge",
        int? ingestGrpcPort = null,
        int? ingestHttpPort = null,
        int? apiPort = null,
        int? dashboardPort = null,
        string? ingestImage = null,
        string? apiImage = null,
        string? dashboardImage = null,
        IResourceBuilder<ParameterResource>? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(imageTag);

        // The FlareResource itself has no process - it's a pure grouping node the five real
        // resources below attach to via WithParentRelationship. It's NOT excluded from the
        // manifest, though: it carries a real ConnectionStringExpression (Flare.Ingest's OTLP
        // gRPC URL) so a downstream `.WithReference(flare)` publishes correctly - if flare were
        // excluded, that reference would emit a dangling `{flare.connectionString}` placeholder
        // pointing at a resource absent from the manifest. WithHidden is a separate, purely
        // dashboard-visibility concern: a consumer adding Flare to their AppHost should see one
        // thing in the resource list - the dashboard - not five implementation-detail backing
        // resources (ClickHouse, its database, Redis, ingest, api); all of it stays reachable
        // via "Show hidden resources" in the dashboard or `aspire describe --include-hidden`.
        var flare = builder.AddResource(new FlareResource(name))
            .WithHidden();

        // ClickHouse: log storage. Same /docker-entrypoint-initdb.d init-script trick as
        // Flare.AppHost/Program.cs, except the SQL is embedded in this package and materialized
        // to a temp directory at call time (see ExtractClickHouseInitScripts) instead of
        // bind-mounted straight from db/clickhouse/ - a consuming repo doesn't have that
        // directory on disk.
        // Pin a fixed, shell-safe password instead of leaving it to AddClickHouse's default
        // random-password parameter - confirmed live that a random password containing
        // '-'/')'/'{'/'}' makes the official image's docker-entrypoint-initdb.d step fail
        // outright (`clickhouse-client ... Code: 552: Unrecognized option
        // '-2B.GBAsjV8)hC_tWe{JNW'`), because that script passes the password straight
        // through to clickhouse-client's CLI arg parser with no escaping for values that
        // look like flags. Same default "flare" docker-compose.yml already uses, for the
        // same reason - and same fix applied to Flare.AppHost/AppHost.cs.
        var clickhousePassword = builder.AddParameter($"{name}-clickhouse-password", "flare", secret: true);
        var clickhouse = builder.AddClickHouse($"{name}-clickhouse", password: clickhousePassword)
            .WithDataVolume()
            .WithBindMount(ExtractClickHouseInitScripts(), "/docker-entrypoint-initdb.d", isReadOnly: true)
            .WithParentRelationship(flare)
            .WithHidden();
        // The Aspire *resource* name is prefixed (collision-safe across multiple AddFlare()
        // calls); the actual ClickHouse database name is pinned to "clickhousedb" - what
        // db/clickhouse/*.sql creates - and re-asserted as the connection-string name on each
        // WithReference below, because Flare.Ingest/Flare.Api's published images hardcode
        // AddClickHouseDataSource(connectionName: "clickhousedb") (confirmed against their
        // Program.cs in Flare's own repo) and won't recognize any other key.
        var logsDb = clickhouse.AddDatabase($"{name}-clickhousedb", databaseName: "clickhousedb")
            .WithHidden();

        // Redis: durable buffer for the batched ClickHouse insert pipeline, so buffered-but-
        // unflushed events survive a Redis container restart. Same interval/threshold as
        // Flare.AppHost/Program.cs.
        var redis = builder.AddRedis($"{name}-redis")
            .WithDataVolume()
            .WithPersistence(interval: TimeSpan.FromSeconds(30), keysChangedThreshold: 100)
            .WithParentRelationship(flare)
            .WithHidden();

        // Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
        // Fixed, unproxied ports so external OTLP clients can point at the conventional port
        // numbers directly, rather than Aspire's dashboard dev-proxy / dynamically-assigned
        // ports - same reasoning as Flare.AppHost/Program.cs.
        var ingest = builder.AddContainer($"{name}-ingest", ingestImage ?? FlareContainerImageTags.IngestImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithEndpoint(port: ingestGrpcPort, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
            .WithEndpoint(port: ingestHttpPort, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
            .WithHttpHealthCheck("/health", endpointName: "otlp-http")
            .WithParentRelationship(flare)
            .WithHidden();
        // "edge" is a mutable tag republished on every push to main - without this, Docker only
        // pulls it once (the default pull policy is "if missing locally") and then silently
        // reuses that stale local image on every future run, forever, with no error. This forces
        // a fresh registry check on every `aspire start` so consumers (including Flare's own
        // examples/ExampleApp.AppHost) actually get current bits. Gated on ingestImage being the
        // default: the ingestImage override above exists specifically so local dev can point at
        // an image built with `docker compose build` that was never pushed to any registry -
        // ImagePullPolicy.Always against a registry-less image would just fail the pull outright.
        if (ingestImage is null)
        {
            ingest.WithImagePullPolicy(ImagePullPolicy.Always);
        }

        // Ingest API key (Planning.md's "Auth + multi-user / roles" item, ingest-side
        // half) - config-driven rather than "create a key via the dashboard," since that
        // manual flow doesn't fit an automated resource-graph-wiring use case like this
        // one. Only wired onto `ingest` - `api`'s own auth (dashboard user sessions) is
        // unrelated to this key.
        if (apiKey is not null)
        {
            ingest
                .WithEnvironment("Auth__IngestKeyRequired", "true")
                .WithEnvironment("Auth__StaticIngestApiKey", apiKey);
        }

        // Attach ingest's real endpoints to the composite FlareResource so consumers can reach
        // them via `flare` itself - through `.WithReference(flare)` (ConnectionStringExpression
        // below) or the WithOtlpEndpoint helper - instead of hand-writing
        // "http://localhost:4317" and hoping local dev topology holds.
        flare.Resource.SetIngestEndpoints(ingest.GetEndpoint("otlp-grpc"), ingest.GetEndpoint("otlp-http"));

        // Flare.Api: the query API (search/filter/time-range/aggregate) and live-tail streaming
        // endpoint over the same clickhousedb.logs table Flare.Ingest writes to. A normal
        // proxied Aspire HTTP endpoint - callers go through Aspire's dev-proxy/service
        // discovery like any other resource.
        var api = builder.AddContainer($"{name}-api", apiImage ?? FlareContainerImageTags.ApiImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithHttpEndpoint(port: apiPort, targetPort: 8080)
            .WithHttpHealthCheck("/health")
            .WithParentRelationship(flare)
            .WithHidden();
        // Same "edge" staleness reasoning and local-dev-override gate as ingest above.
        if (apiImage is null)
        {
            api.WithImagePullPolicy(ImagePullPolicy.Always);
        }

        // Flare.Dashboard: the SvelteKit SPA. PUBLIC_API_URL/ORIGIN are read at *container
        // runtime* via SvelteKit's $env/dynamic/public, not baked in at image build time
        // (confirmed against src/dashboard/src/lib/api.ts in Flare's own repo), so this one
        // published image is reconfigurable per consumer with zero rework. ORIGIN references
        // the dashboard's own endpoint - it has to know its own externally-reachable URL for
        // SvelteKit's Node adapter to accept requests. Both are read by *the browser*, not by
        // another container, so both are pinned to the localhost/loopback network context
        // (KnownNetworkIdentifiers.LocalhostNetwork) rather than the default container-network
        // resolution GetEndpoint uses for a plain container-to-container reference - confirmed
        // by e2e run that the default otherwise injects Aspire's internal *.dev.internal DNS
        // names, unreachable from a real browser on the host.
        var dashboard = builder.AddContainer($"{name}-dashboard", dashboardImage ?? FlareContainerImageTags.DashboardImage, imageTag)
            .WaitFor(api)
            .WithHttpEndpoint(port: dashboardPort, targetPort: 3000)
            // A real liveness signal - the container reaching "Running" doesn't mean SvelteKit's
            // Node server is actually accepting requests yet. WaitForFlare (below) waits on this,
            // not just the container's Running state.
            .WithHttpHealthCheck("/")
            .WithParentRelationship(flare);
        // Same "edge" staleness reasoning and local-dev-override gate as ingest above - this is
        // the one that actually bit us: a consumer's Docker cache pins a stale dashboard build
        // indefinitely otherwise, with nothing on screen telling them why they're not seeing
        // recent dashboard changes.
        if (dashboardImage is null)
        {
            dashboard.WithImagePullPolicy(ImagePullPolicy.Always);
        }
        dashboard
            .WithEnvironment("PUBLIC_API_URL", api.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork))
            .WithEnvironment("ORIGIN", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));

        // Flare.Api rejects every browser origin by default once auth is in the picture
        // (Cors:AllowedOrigins has no safe default - see docs/auth.md in Flare's own
        // repo) - without this, every fetch the dashboard's browser-side JS makes fails
        // CORS and the app hangs on its own "checking session" spinner with no
        // indication why. Same LocalhostNetwork-pinned endpoint reference already used
        // for PUBLIC_API_URL above and for the same reason: this has to resolve to what
        // the *browser* sees, not container-network DNS. Confirmed live against a real
        // Aspire-orchestrated run that this was missing and broke the dashboard outright.
        api.WithEnvironment("Cors__AllowedOrigins__0", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));

        // Stash the dashboard's resource name so WaitForFlare can look it up later without the
        // caller needing to hold onto a `dashboard` variable of their own - see WaitForFlare's
        // remarks for why `.WaitFor(flare)` itself can never work here.
        flare.Resource.SetDashboardResourceName(dashboard.Resource.Name);

        return flare;
    }

    /// <summary>
    /// Waits for the whole Flare stack - ClickHouse, Redis, ingest, api, and the dashboard - to be
    /// ready before starting <paramref name="builder"/>'s resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain <c>.WaitFor(flare)</c> does <em>not</em> work, no matter what health checks are
    /// attached to <c>flare</c> itself: <see cref="FlareResource"/> implements
    /// <see cref="IResourceWithoutLifetime"/> (it's a pure grouping node - see its type doc
    /// comment), and Aspire's own <c>ResourceNotificationService.WaitForDependenciesAsync</c>
    /// unconditionally filters out any <c>WaitFor</c> target that implements
    /// <see cref="IResourceWithoutLifetime"/> <em>before</em> it ever looks at that target's
    /// state or health - confirmed against Aspire's own source
    /// (<c>src/Aspire.Hosting/ApplicationModel/ResourceNotificationService.cs</c>,
    /// <c>waitAnnotation.Resource is not IResourceWithoutLifetime</c>). So a downstream
    /// <c>.WaitFor(flare)</c> is treated as having nothing to wait for and resolves immediately,
    /// regardless of whether ClickHouse/Redis/ingest/api/the dashboard have even started.
    /// </para>
    /// <para>
    /// This method sidesteps that by waiting on the dashboard container instead - a real,
    /// DCP-managed resource with its own lifetime and a genuine HTTP health check. The dashboard
    /// already <c>.WaitFor(api)</c>s, and <c>api</c>/<c>ingest</c> already wait on ClickHouse and
    /// Redis, so "dashboard healthy" transitively means the whole stack is up.
    /// </para>
    /// </remarks>
    /// <typeparam name="TDestination">The type of the resource that will be waiting.</typeparam>
    /// <param name="builder">The resource builder for the resource that will be waiting.</param>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <returns>The <paramref name="builder"/>, for chaining.</returns>
    public static IResourceBuilder<TDestination> WaitForFlare<TDestination>(
        this IResourceBuilder<TDestination> builder,
        IResourceBuilder<FlareResource> flare)
        where TDestination : IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(flare);

        var dashboard = flare.ApplicationBuilder.CreateResourceBuilder<ContainerResource>(flare.Resource.DashboardResourceName);
        return builder.WaitFor(dashboard);
    }

    /// <summary>
    /// Points a consuming resource's OTLP exporter at Flare.Ingest by setting
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> from <paramref name="flare"/>'s ingest sub-resource -
    /// resolved correctly per execution context (loopback locally, container-network alias
    /// under compose, real Service DNS/ingress once published) instead of a hardcoded string.
    /// Prefer this over a hand-written
    /// <c>.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")</c>.
    /// </summary>
    /// <param name="builder">The consuming resource builder.</param>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="useHttp">
    /// Use Flare.Ingest's OTLP/HTTP endpoint (4318) instead of OTLP/gRPC (4317, the default -
    /// matches the default protocol OpenTelemetry .NET's OTLP exporter uses).
    /// </param>
    /// <returns>The <paramref name="builder"/>, for chaining.</returns>
    public static IResourceBuilder<TDestination> WithOtlpEndpoint<TDestination>(
        this IResourceBuilder<TDestination> builder,
        IResourceBuilder<FlareResource> flare,
        bool useHttp = false)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(flare);

        var endpoint = useHttp ? flare.Resource.OtlpHttpEndpoint : flare.Resource.OtlpGrpcEndpoint;
        return builder.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint);
    }

    /// <summary>
    /// Writes this package's embedded ClickHouse init scripts (<c>db/clickhouse/*.sql</c> in
    /// Flare's own repo) to a fresh temp directory and returns its absolute path.
    /// </summary>
    /// <remarks>
    /// <c>WithBindMount</c> resolves a relative source path against the *consumer's* AppHost
    /// project directory, not this package's - an absolute path sidesteps that entirely, and
    /// happens to also be how the source path always needs to work regardless of which project
    /// calls <c>AddFlare</c> or from where.
    /// </remarks>
    private static string ExtractClickHouseInitScripts()
    {
        const string ResourcePrefix = "Aspire.Hosting.Flare.ClickHouseInit.";

        var assembly = typeof(FlareResourceBuilderExtensions).Assembly;
        var tempDir = Path.Combine(Path.GetTempPath(), "flare-clickhouse-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[ResourcePrefix.Length..];
            using var resourceStream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was listed but could not be opened.");
            using var fileStream = File.Create(Path.Combine(tempDir, fileName));
            resourceStream.CopyTo(fileStream);
        }

        return tempDir;
    }
}

/// <summary>
/// Docker Hub image coordinates for Flare's three published components (see
/// <c>.github/workflows/docker-publish.yml</c> in Flare's own repo). Unqualified Docker Hub
/// image names - registry defaults to docker.io.
/// </summary>
internal static class FlareContainerImageTags
{
    internal const string IngestImage = "xracer007/flare-ingest";
    internal const string ApiImage = "xracer007/flare-api";
    internal const string DashboardImage = "xracer007/flare-dashboard";
}
