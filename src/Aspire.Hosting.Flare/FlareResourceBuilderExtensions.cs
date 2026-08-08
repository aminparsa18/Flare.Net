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
    /// <returns>An <see cref="IResourceBuilder{FlareResource}"/> for the composite Flare resource.</returns>
    /// <exception cref="ArgumentException"><paramref name="imageTag"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> AddFlare(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "flare",
        string imageTag = "edge",
        int? ingestGrpcPort = null,
        int? ingestHttpPort = null,
        int? apiPort = null,
        int? dashboardPort = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(imageTag);

        // The FlareResource itself has no process - it's a pure grouping node the five real
        // resources below attach to via WithParentRelationship. ExcludeFromManifest because
        // there's nothing meaningful to publish for a node that doesn't run anything itself;
        // the five resources it groups still publish normally.
        var flare = builder.AddResource(new FlareResource(name))
            .ExcludeFromManifest();

        // ClickHouse: log storage. Same /docker-entrypoint-initdb.d init-script trick as
        // Flare.AppHost/Program.cs, except the SQL is embedded in this package and materialized
        // to a temp directory at call time (see ExtractClickHouseInitScripts) instead of
        // bind-mounted straight from db/clickhouse/ - a consuming repo doesn't have that
        // directory on disk.
        var clickhouse = builder.AddClickHouse($"{name}-clickhouse")
            .WithDataVolume()
            .WithBindMount(ExtractClickHouseInitScripts(), "/docker-entrypoint-initdb.d", isReadOnly: true)
            .WithParentRelationship(flare);
        // The Aspire *resource* name is prefixed (collision-safe across multiple AddFlare()
        // calls); the actual ClickHouse database name is pinned to "clickhousedb" - what
        // db/clickhouse/*.sql creates - and re-asserted as the connection-string name on each
        // WithReference below, because Flare.Ingest/Flare.Api's published images hardcode
        // AddClickHouseDataSource(connectionName: "clickhousedb") (confirmed against their
        // Program.cs in Flare's own repo) and won't recognize any other key.
        var logsDb = clickhouse.AddDatabase($"{name}-clickhousedb", databaseName: "clickhousedb");

        // Redis: durable buffer for the batched ClickHouse insert pipeline, so buffered-but-
        // unflushed events survive a Redis container restart. Same interval/threshold as
        // Flare.AppHost/Program.cs.
        var redis = builder.AddRedis($"{name}-redis")
            .WithDataVolume()
            .WithPersistence(interval: TimeSpan.FromSeconds(30), keysChangedThreshold: 100)
            .WithParentRelationship(flare);

        // Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
        // Fixed, unproxied ports so external OTLP clients can point at the conventional port
        // numbers directly, rather than Aspire's dashboard dev-proxy / dynamically-assigned
        // ports - same reasoning as Flare.AppHost/Program.cs.
        var ingest = builder.AddContainer($"{name}-ingest", FlareContainerImageTags.IngestImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithEndpoint(port: ingestGrpcPort, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
            .WithEndpoint(port: ingestHttpPort, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
            .WithHttpHealthCheck("/health", endpointName: "otlp-http")
            .WithParentRelationship(flare);

        // Flare.Api: the query API (search/filter/time-range/aggregate) and live-tail streaming
        // endpoint over the same clickhousedb.logs table Flare.Ingest writes to. A normal
        // proxied Aspire HTTP endpoint - callers go through Aspire's dev-proxy/service
        // discovery like any other resource.
        var api = builder.AddContainer($"{name}-api", FlareContainerImageTags.ApiImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithHttpEndpoint(port: apiPort, targetPort: 8080)
            .WithHttpHealthCheck("/health")
            .WithParentRelationship(flare);

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
        var dashboard = builder.AddContainer($"{name}-dashboard", FlareContainerImageTags.DashboardImage, imageTag)
            .WaitFor(api)
            .WithHttpEndpoint(port: dashboardPort, targetPort: 3000)
            .WithParentRelationship(flare);
        dashboard
            .WithEnvironment("PUBLIC_API_URL", api.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork))
            .WithEnvironment("ORIGIN", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));

        return flare;
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
