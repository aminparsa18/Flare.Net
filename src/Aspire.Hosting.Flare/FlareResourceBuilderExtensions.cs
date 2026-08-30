using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.Kubernetes.Resources;

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
    /// The tag to pull for all three Flare images. Defaults to <c>"0.2.0"</c>, the latest
    /// stable Flare release this package version was tested against - deliberately NOT
    /// Docker Hub's floating <c>latest</c>/<c>edge</c> tags, so a given
    /// <c>Flare.Hosting.Aspire</c> NuGet version keeps pulling the same images forever
    /// instead of silently changing behavior as new Flare releases ship. This default is
    /// bumped as part of cutting each new <c>Flare.Hosting.Aspire</c> release, once that
    /// release has been tested against a newer Flare image - it does not track Docker
    /// Hub automatically. Pass <c>"edge"</c> yourself to track Flare's unreleased
    /// <c>main</c> branch instead.
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
    /// <param name="enableResourceGraph">
    /// Turns on the dashboard's Resources page (a live topology graph) for this Flare
    /// instance. Off by default - real, meaningful cluster/Docker access is involved (see
    /// below), and this package follows the same "absent config = off" pattern as
    /// <paramref name="apiKey"/> rather than defaulting it on. Which of the two topology
    /// providers this actually wires up is picked automatically from which compute
    /// environment is registered - see the "Opt-in Resources page" block inside this
    /// method for the exact branch - not a separate parameter, since a given AppHost only
    /// ever targets one deployment environment at a time:
    /// <list type="bullet">
    /// <item>
    /// <b>Docker</b> (the default for local <c>aspire run</c>, and for a Docker Compose
    /// publish target): adds one more sidecar container
    /// (<c>tecnativa/docker-socket-proxy</c>, scoped to read-only container list/inspect -
    /// no exec, no start/stop, no image/volume/network management) with
    /// <c>/var/run/docker.sock</c> bind-mounted read-only into it, and points <c>api</c>'s
    /// <c>DockerResources__ProxyUrl</c> at it. Flare.Api itself never touches the socket
    /// directly, only this proxy.
    /// </item>
    /// <item>
    /// <b>Kubernetes</b> (when a <c>KubernetesEnvironmentResource</c> is registered): no
    /// sidecar - instead attaches a namespace-scoped, read-only RBAC
    /// <c>ServiceAccount</c>/<c>Role</c>/<c>RoleBinding</c> (<c>get</c>/<c>list</c>/<c>watch</c>
    /// on <c>pods</c>/<c>services</c> only) to <c>api</c>'s own generated <c>Deployment</c>,
    /// and points <c>api</c>'s <c>KubernetesResources__Enabled</c> at <c>true</c>.
    /// </item>
    /// </list>
    /// Resource-graph identity labels (<c>flare.resource</c>/<c>flare.role</c>/
    /// <c>flare.relationships</c> - Docker container labels, or Kubernetes pod-template
    /// labels) are applied regardless of this flag - they're inert metadata with no effect
    /// unless something is actually reading the Docker/Kubernetes API, so there's no
    /// reason to gate them separately. See <c>docs/aspire-hosting.md</c>'s Resources-page
    /// section for the full security rationale (same one <c>docker-compose.yml</c>'s own
    /// Docker opt-in documents).
    /// </param>
    /// <param name="publicApiUrl">
    /// The externally-reachable URL browsers should use to reach <c>api</c>, surfaced to the
    /// dashboard as <c>PUBLIC_API_URL</c>. Left unset (the default), this stays pinned to
    /// <c>api</c>'s own loopback endpoint - correct for <c>aspire run</c>, where the dashboard
    /// and the browser viewing it are on the same machine. Once actually publishing/deploying
    /// (<c>aspire publish</c>/<c>aspire deploy</c> against a Docker Compose target - see
    /// <c>docs/aspire-hosting.md</c>'s "Publishing / deploying via aspire publish" section) that
    /// assumption stops holding - the browser reaches the deployed stack by a real hostname/IP,
    /// not <c>localhost</c> - so pass a <c>secret: false</c> <c>AddParameter</c> result here
    /// (left unset, so Aspire captures it as an <c>.env.{environment}</c> placeholder an
    /// operator fills in with the real deployed URL per environment) instead.
    /// </param>
    /// <param name="publicDashboardUrl">
    /// The externally-reachable URL browsers should use to reach the dashboard itself, surfaced
    /// as the dashboard's own <c>ORIGIN</c> (required by SvelteKit's Node adapter to accept
    /// requests) and as <c>api</c>'s <c>Cors__AllowedOrigins__0</c> (so <c>api</c> accepts
    /// browser requests originating from it). Same default/override story as
    /// <paramref name="publicApiUrl"/> - unset keeps today's loopback-pinned <c>aspire run</c>
    /// behavior; set it via an <c>AddParameter</c> result for publish/deploy.
    /// </param>
    /// <returns>An <see cref="IResourceBuilder{FlareResource}"/> for the composite Flare resource.</returns>
    /// <exception cref="ArgumentException"><paramref name="imageTag"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> AddFlare(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "flare",
        string imageTag = "0.2.0",
        int? ingestGrpcPort = null,
        int? ingestHttpPort = null,
        int? apiPort = null,
        int? dashboardPort = null,
        string? ingestImage = null,
        string? apiImage = null,
        string? dashboardImage = null,
        IResourceBuilder<ParameterResource>? apiKey = null,
        bool enableResourceGraph = false,
        IResourceBuilder<ParameterResource>? publicApiUrl = null,
        IResourceBuilder<ParameterResource>? publicDashboardUrl = null)
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
        var clickhouse = builder.AddClickHouse($"{name}-clickhouse", password: clickhousePassword);
        // Init scripts used to be a bind mount from a temp directory this process extracted
        // them to (WithBindMount(ExtractClickHouseInitScripts(), ...)) - fine for `aspire run`,
        // but that path only exists on the machine that ran `aspire publish`/`aspire build`, not
        // on whatever host the generated docker-compose.yaml actually runs on (see
        // docs/aspire-hosting.md's "Publishing / deploying via aspire publish" section). Baking
        // the scripts into a small custom image built FROM AddClickHouse's own resolved image
        // (WithDockerfile, not a bind mount) makes this portable to any Docker host, at the cost
        // of `aspire run` now needing local `docker build` capability too, not just pull/run.
        clickhouse
            .WithDockerfile(WriteClickHouseInitDockerContext(clickhouse.Resource))
            .WithDataVolume($"{name}-clickhouse-data")
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("clickhouse");
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
            .WithDataVolume($"{name}-redis-data")
            .WithPersistence(interval: TimeSpan.FromSeconds(30), keysChangedThreshold: 100)
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("redis");

        // Auth's identity store (Users/Sessions/IngestApiKeys) - embedded SQLite (see
        // docs/auth.md's "why not a fourth backing-store service" for the design
        // rationale), shared between ingest and api via a plain named volume rather than
        // a dedicated Aspire-modeled resource with its own container. WithVolume (not
        // WithDataVolume, which is ClickHouse/Redis's own integration-specific
        // convenience method, only available on their specialized resource types) is the
        // generic mechanism for a raw AddContainer resource - passing the *same* volume
        // name to both ingest and api below is what makes Docker give them the literal
        // same file, mirroring docker-compose.yml's identity-data volume. Without this,
        // the SQLite file lives in each container's ephemeral writable layer and is wiped
        // on every container recreation - confirmed live: this exact gap surfaced as "asks
        // to create the admin account again every time I restart aspire."
        var identityVolumeName = $"{name}-identity-data";
        const string identityDbPath = "/data/identity/flare-identity.db";

        // Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
        // Fixed, unproxied ports so external OTLP clients can point at the conventional port
        // numbers directly, rather than Aspire's dashboard dev-proxy / dynamically-assigned
        // ports - same reasoning as Flare.AppHost/Program.cs.
        var ingest = builder.AddContainer($"{name}-ingest", ingestImage ?? FlareContainerImageTags.IngestImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithVolume(identityVolumeName, "/data/identity")
            .WithEnvironment("Identity__DbPath", identityDbPath)
            .WithEndpoint(port: ingestGrpcPort, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
            .WithEndpoint(port: ingestHttpPort, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
            .WithHttpHealthCheck("/health", endpointName: "otlp-http")
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("ingest", "clickhouse:Reference,redis:Reference");
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
            // Same volume name as ingest above - see that assignment's remarks.
            .WithVolume(identityVolumeName, "/data/identity")
            .WithEnvironment("Identity__DbPath", identityDbPath)
            .WithHttpEndpoint(port: apiPort, targetPort: 8080)
            .WithHttpHealthCheck("/health")
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("api", "clickhouse:Reference,redis:Reference");
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
            .WithParentRelationship(flare)
            .WithFlareResourceLabels("dashboard", "api:Reference");
        // Same "edge" staleness reasoning and local-dev-override gate as ingest above - this is
        // the one that actually bit us: a consumer's Docker cache pins a stale dashboard build
        // indefinitely otherwise, with nothing on screen telling them why they're not seeing
        // recent dashboard changes.
        if (dashboardImage is null)
        {
            dashboard.WithImagePullPolicy(ImagePullPolicy.Always);
        }
        // publicApiUrl/publicDashboardUrl (both unset by default) exist for exactly this
        // localhost assumption breaking once actually deployed rather than `aspire run` - see
        // their doc comments on AddFlare and docs/aspire-hosting.md's "Publishing / deploying
        // via aspire publish" section.
        if (publicApiUrl is not null)
        {
            dashboard.WithEnvironment("PUBLIC_API_URL", publicApiUrl);
        }
        else
        {
            dashboard.WithEnvironment("PUBLIC_API_URL", api.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));
        }

        // Flare.Api rejects every browser origin by default once auth is in the picture
        // (Cors:AllowedOrigins has no safe default - see docs/auth.md in Flare's own
        // repo) - without this, every fetch the dashboard's browser-side JS makes fails
        // CORS and the app hangs on its own "checking session" spinner with no
        // indication why. Same LocalhostNetwork-pinned endpoint reference already used
        // for PUBLIC_API_URL above and for the same reason: this has to resolve to what
        // the *browser* sees, not container-network DNS. Confirmed live against a real
        // Aspire-orchestrated run that this was missing and broke the dashboard outright.
        if (publicDashboardUrl is not null)
        {
            dashboard.WithEnvironment("ORIGIN", publicDashboardUrl);
            api.WithEnvironment("Cors__AllowedOrigins__0", publicDashboardUrl);
        }
        else
        {
            dashboard.WithEnvironment("ORIGIN", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));
            api.WithEnvironment("Cors__AllowedOrigins__0", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));
        }

        // Opt-in Resources page (docs/aspire-hosting.md) - see enableResourceGraph's doc
        // comment for the full rationale. Exactly one of the two topology providers gets wired
        // per deploy, picked by which compute environment is actually present AND whether this
        // is actually a publish/deploy pass: Kubernetes only when both
        // builder.ExecutionContext.IsPublishMode is true (aspire publish/aspire deploy - never
        // aspire run) AND a KubernetesEnvironmentResource is registered, Docker (the historical/
        // local aspire-run behavior) otherwise. The IsPublishMode check matters on its own,
        // separately from which environment is registered: a consumer's AppHost commonly
        // registers AddKubernetesEnvironment once and keeps using `aspire run` day-to-day for
        // the inner dev loop - `aspire run` always executes real Docker containers via DCP
        // regardless of what deployment-target resources happen to be registered (same
        // "environment resources don't affect aspire run" behavior WithFlareResourceLabels's
        // PublishAsDockerComposeService/PublishAsKubernetesService calls already get for free,
        // since those specific APIs are inherently publish-only - but AddContainer/WithEnvironment
        // below are NOT, so this block needs the explicit check they don't). Without it, that
        // ordinary "registered for later deploy, but running locally today" shape would wire
        // KubernetesResources__Enabled onto a flare-api that's actually talking to real local
        // Docker containers, breaking the Resources page for the entire duration of every
        // `aspire run` session. Deliberately mutually exclusive, not "wire both" - the Docker
        // branch's socket-proxy sidecar bind-mounts /var/run/docker.sock, which is meaningless
        // (no such socket exists on a Kubernetes node the way it does on a Docker host) and a
        // real privilege-escalation footgun to even attempt shipping into a cluster, so it must
        // never be created when publishing/deploying to Kubernetes. Confirmed live (2026-08-30,
        // this feature's own live e2e pass against a local k3s cluster) that `aspire deploy`
        // does set IsPublishMode the same way `aspire publish` does - the Kubernetes branch
        // fired correctly (RBAC generated, no Docker sidecar created).
        if (enableResourceGraph)
        {
            var targetingKubernetes = builder.ExecutionContext.IsPublishMode
                && builder.Resources.OfType<KubernetesEnvironmentResource>().Any();

            if (targetingKubernetes)
            {
                // Kubernetes: no sidecar container needed - KubernetesResources.KubernetesResourcePoller
                // talks to the Kubernetes API server directly via api's own ServiceAccount,
                // scoped by the namespace-only, read-only Role below (Planning.md's Kubernetes
                // resource-topology item - "no live Deployment API read" scope trim means only
                // pods/services need to be readable, not deployments/replicasets - see
                // KubernetesResourcePoller's remarks).
                api.WithEnvironment("KubernetesResources__Enabled", "true");
                api.PublishAsKubernetesService(resource =>
                {
                    // Each of the three needs a distinct Metadata.Name, not just a distinct
                    // Kind - confirmed live (2026-08-30, this feature's own live e2e pass)
                    // that Aspire's per-object Helm-chart-template-file naming keys purely off
                    // Metadata.Name, not name+kind. All three sharing the literal same name
                    // string (as this originally did) meant each AdditionalResources.Add call
                    // silently overwrote the previous one's rendered template file - only the
                    // last one added (RoleBinding) actually made it into the chart, so the
                    // ServiceAccount/Role it referenced never existed on the cluster and
                    // flare-api's own ReplicaSet couldn't create pods at all ("serviceaccount
                    // ... not found").
                    var serviceAccount = new ServiceAccountV1();
                    serviceAccount.Metadata.Name = $"{name}-resource-graph";

                    var role = new Role();
                    role.Metadata.Name = $"{name}-resource-graph-role";
                    role.Rules.Add(new PolicyRuleV1
                    {
                        ApiGroups = { "" }, // core API group.
                        Resources = { "pods", "services" },
                        Verbs = { "get", "list", "watch" },
                    });

                    var roleBinding = new RoleBinding();
                    roleBinding.Metadata.Name = $"{name}-resource-graph-binding";
                    roleBinding.RoleRef = new RoleRefV1
                    {
                        ApiGroup = "rbac.authorization.k8s.io",
                        Kind = "Role",
                        Name = role.Metadata.Name,
                    };
                    roleBinding.Subjects.Add(new SubjectV1
                    {
                        Kind = "ServiceAccount",
                        Name = serviceAccount.Metadata.Name,
                        // A RoleBinding subject's namespace isn't optional for a ServiceAccount
                        // kind (the RBAC authorizer matches on the full
                        // system:serviceaccount:<namespace>:<name> identity) - this Helm
                        // built-in resolves to whatever namespace `aspire deploy`/`helm
                        // upgrade --install` actually targets, since the ServiceAccount/Role/
                        // RoleBinding/Deployment below are all rendered into that same release's
                        // chart. Confirmed live (2026-08-30) that Aspire's per-object YAML
                        // templating passes this string through unescaped and Helm resolves it
                        // correctly - the RoleBinding applied cleanly and the ServiceAccount it
                        // references was found (once the naming-collision bug below was fixed).
                        Namespace = "{{ .Release.Namespace }}",
                    });

                    resource.AdditionalResources.Add(serviceAccount);
                    resource.AdditionalResources.Add(role);
                    resource.AdditionalResources.Add(roleBinding);

                    // Second PublishAsKubernetesService call on this same `api` builder -
                    // WithFlareResourceLabels("api", ...) above already made one, for the
                    // flare.* pod-template labels. Confirmed live (2026-08-30) that these
                    // compose independently rather than the second overwriting the first -
                    // the deployed api Pod carried both the flare.* labels/annotations and this
                    // ServiceAccountName. RBAC only ever attaches to api specifically (always a
                    // Deployment, never promoted to a StatefulSet - it has no WithDataVolume()
                    // call), so the Deployment-only pattern match here is intentional, unlike
                    // the Workload-general one above.
                    if (resource.Workload is Deployment deployment)
                    {
                        deployment.Spec.Template.Spec.ServiceAccountName = serviceAccount.Metadata.Name;
                    }
                });
            }
            else
            {
                // Docker (docs/aspire-hosting.md) - deliberately mirrors docker-compose.yml's
                // own docker-proxy service: same image, same CONTAINERS=1-only scoping, same
                // read-only socket bind mount, off unless explicitly requested.
                var dockerProxy = builder.AddContainer($"{name}-docker-proxy", FlareContainerImageTags.DockerProxyImage)
                    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", isReadOnly: true)
                    .WithEnvironment("CONTAINERS", "1")
                    .WithEnvironment("POST", "0")
                    .WithHttpEndpoint(targetPort: 2375)
                    .WithParentRelationship(flare)
                    .WithHidden();

                api.WithEnvironment("DockerResources__ProxyUrl", dockerProxy.GetEndpoint("http"));
            }
        }

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
    /// Applies this package's resource-graph identity labels
    /// (<c>flare.resource</c>/<c>flare.role</c>/<c>flare.relationships</c>) to a
    /// container resource - the Aspire/DCP-side counterpart to
    /// <c>docker-compose.yml</c>'s own <c>labels:</c> blocks, same label vocabulary, now shared
    /// by both topology providers (Planning.md's Kubernetes resource-topology item). Applied
    /// up to three ways since no single one covers every run mode: <c>WithContainerRuntimeArgs</c>
    /// (raw <c>docker run</c> arguments - there's no more-direct "add a Docker label" API in
    /// Aspire 13.4) for local <c>aspire run</c>/DCP, <c>PublishAsDockerComposeService</c> for
    /// <c>aspire publish</c>'s generated Docker Compose output, and <c>PublishAsKubernetesService</c>
    /// for its generated Kubernetes output - each publish-time call has no concept of the
    /// others' output at all, so skipping any one of them silently ships that target with no
    /// labels and breaks the Resources page's topology graph on it.
    /// </summary>
    /// <remarks>
    /// The <c>PublishAsDockerComposeService</c>/<c>PublishAsKubernetesService</c> calls are each
    /// conditional on the matching environment resource
    /// (<see cref="DockerComposeEnvironmentResource"/>/<c>KubernetesEnvironmentResource</c>)
    /// actually being present in the model - NOT unconditional the way
    /// <c>WithContainerRuntimeArgs</c> above is. Confirmed live (2026-08-29, verifying
    /// Kubernetes publish support - see Planning.md's "Helm chart for Kubernetes" item):
    /// calling <c>PublishAsDockerComposeService</c> at all, even on an AppHost that never adds
    /// a Docker Compose environment, unconditionally registers Aspire's own
    /// <c>validate-docker-compose</c> pipeline step - which then hard-fails <em>any</em>
    /// <c>aspire publish</c>/<c>aspire deploy</c>, regardless of target (Kubernetes, Azure, AWS,
    /// ...), with "Resource '...' is configured to publish as a Docker Compose service, but
    /// there are no 'DockerComposeEnvironmentResource' resources." Before this guard, that
    /// meant <c>AddFlare</c> could only ever be published to Docker Compose - publishing to
    /// anything else crashed outright, not just silently missing labels. The Kubernetes branch
    /// is gated the same way defensively, on the same reasoning, even though it hasn't been
    /// confirmed to fail the identical way unguarded. Gating on the matching environment
    /// resource being present requires the consumer to call
    /// <c>AddDockerComposeEnvironment(...)</c>/<c>AddKubernetesEnvironment(...)</c> before
    /// <c>AddFlare(...)</c> (already the documented/example order - see
    /// <c>docs/aspire-hosting.md</c> and <c>examples/ExampleApp.AppHost/Program.cs</c>) -
    /// <c>builder.Resources</c> is checked synchronously at the point each Flare sub-resource is
    /// built, so an environment added after <c>AddFlare</c> returns would not be seen.
    /// <para>
    /// The Kubernetes branch stamps <c>flare.resource</c>/<c>flare.role</c> onto the generated
    /// <em>pod template labels</em> (<c>resource.Workload.PodTemplate.Metadata.Labels</c>), not
    /// the workload object's own metadata - these land on the real Pods that way, giving
    /// <c>KubernetesResources.KubernetesResourcePoller</c> (which lists Pods, not
    /// Deployments/StatefulSets - see that type's remarks) the same stable <c>flare.role</c>
    /// identity anchor the Docker provider already has via Flare.Api's own
    /// <c>ResourceNodeDto.Role</c>. Confirmed live (2026-08-30, this feature's own live e2e pass
    /// against a local k3s cluster) that Aspire's Kubernetes publisher does not overwrite or
    /// merge these away before the chart is rendered - the hard way, twice: (1) this reads
    /// <c>resource.Workload</c>'s common <c>Workload.PodTemplate</c>, not a
    /// <c>resource.Workload is Deployment</c> pattern match, specifically because ClickHouse/
    /// Redis's <c>WithDataVolume()</c> calls promote them to a <c>StatefulSet</c> under
    /// Kubernetes (see docs/aspire-hosting.md's persistent-volumes bullet) - the original
    /// Deployment-only check silently skipped them, leaving both with zero <c>flare.*</c>
    /// labels/annotations at all, invisible to the topology graph entirely; (2)
    /// <c>flare.relationships</c> goes onto pod-template <em>annotations</em> instead of
    /// labels, because a Kubernetes label VALUE has a strict charset (roughly
    /// alphanumeric/<c>-</c>/<c>_</c>/<c>.</c> only - no <c>:</c>/<c>,</c>) that a
    /// <c>"clickhouse:Reference,redis:Reference"</c>-shaped value violates outright -
    /// <c>helm upgrade --install</c> rejected the whole Deployment as invalid the first time
    /// this was actually deployed to a real cluster. Docker labels have no such restriction,
    /// which is why neither of these surfaced there. Annotations have no charset restriction,
    /// and this value is never selected on anyway (only <c>flare.resource</c>/<c>flare.role</c>
    /// are, by <c>KubernetesResourcePoller</c>'s label-selector list call) - see
    /// <c>KubernetesResourcePoller.BuildSnapshot</c>'s matching remark for the read side.
    /// </para>
    /// </remarks>
    /// <param name="builder">The container resource to label.</param>
    /// <param name="role">This container's stable <c>flare.role</c> value (e.g. <c>"ingest"</c>) - what Flare.Api's own <c>ResourceNodeDto.Role</c> reads back.</param>
    /// <param name="relationships">Raw <c>flare.relationships</c> value (e.g. <c>"clickhouse:Reference,redis:Reference"</c> - a label on Docker/Docker Compose, an annotation on Kubernetes, see the remarks), or <see langword="null"/> to omit it entirely (nothing this container references).</param>
    private static IResourceBuilder<T> WithFlareResourceLabels<T>(this IResourceBuilder<T> builder, string role, string? relationships = null)
        where T : ContainerResource
    {
        var args = new List<string> { "--label", "flare.resource=true", "--label", $"flare.role={role}" };
        if (relationships is not null)
        {
            args.Add("--label");
            args.Add($"flare.relationships={relationships}");
        }

        builder.WithContainerRuntimeArgs([.. args]);

        if (builder.ApplicationBuilder.Resources.OfType<DockerComposeEnvironmentResource>().Any())
        {
            builder.PublishAsDockerComposeService((_, service) =>
            {
                service.Labels["flare.resource"] = "true";
                service.Labels["flare.role"] = role;
                if (relationships is not null)
                {
                    service.Labels["flare.relationships"] = relationships;
                }
            });
        }

        if (builder.ApplicationBuilder.Resources.OfType<KubernetesEnvironmentResource>().Any())
        {
            builder.PublishAsKubernetesService(resource =>
            {
                // Workload (not Deployment specifically) - confirmed live (2026-08-30, this
                // feature's own live e2e pass) that ClickHouse/Redis's WithDataVolume() calls
                // promote them to a StatefulSet under Kubernetes (see docs/aspire-hosting.md's
                // persistent-volumes bullet), which the original Deployment-only pattern match
                // here silently skipped entirely - ClickHouse/Redis got zero flare.* labels at
                // all, invisible to KubernetesResources.KubernetesResourcePoller's
                // flare.resource=true selector. PodTemplate is declared on the common Workload
                // base (Deployment/StatefulSet both derive from it), so this now covers both -
                // and anything else Aspire's Kubernetes publisher might promote a workload to
                // in the future.
                if (resource.Workload is not { } workload)
                {
                    return;
                }

                var labels = workload.PodTemplate.Metadata.Labels;
                labels["flare.resource"] = "true";
                labels["flare.role"] = role;
                if (relationships is not null)
                {
                    // Confirmed live (2026-08-30, this feature's own live e2e pass) that a
                    // Kubernetes label VALUE has a strict charset
                    // ([A-Za-z0-9][-A-Za-z0-9_.]*[A-Za-z0-9] - no ':'/',') that
                    // "clickhouse:Reference,redis:Reference"-shaped relationship values
                    // violate outright - `helm upgrade --install` rejects the whole
                    // Deployment as invalid, unlike Docker labels above, which have no such
                    // restriction. An annotation has no charset restriction, so
                    // flare.relationships goes there instead on the Kubernetes side only -
                    // it was never meant to be selected on anyway (only flare.resource/
                    // flare.role are, by KubernetesResources.KubernetesResourcePoller's
                    // label-selector list call), so moving just this one value off Labels
                    // doesn't affect discovery at all.
                    workload.PodTemplate.Metadata.Annotations["flare.relationships"] = relationships;
                }
            });
        }

        return builder;
    }

    /// <summary>
    /// Writes this package's embedded ClickHouse init scripts (<c>db/clickhouse/*.sql</c> in
    /// Flare's own repo) plus a generated <c>Dockerfile</c> into a fresh temp directory, and
    /// returns that directory's absolute path as a <c>WithDockerfile</c> build context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build context, not a bind mount - the ClickHouse image's own
    /// <c>docker-entrypoint-initdb.d</c> convention runs any <c>*.sql</c> file found there once,
    /// on first startup against an empty data directory, so <c>COPY</c>-ing them in at build
    /// time has the exact same effect as the old bind mount did, but the resulting image is
    /// self-contained - portable to whatever Docker host actually runs
    /// <c>docker compose up</c>, unlike a bind mount from this (the <c>aspire publish</c>-time)
    /// machine's own temp directory.
    /// </para>
    /// <para>
    /// The generated Dockerfile's <c>FROM</c> line is read off <paramref name="clickhouseResource"/>'s
    /// own resolved container image via <c>TryGetContainerImageName</c> rather than hand-pinned
    /// here, so this never drifts from whatever <c>Aspire.Hosting.ClickHouse</c>'s own
    /// <c>AddClickHouse</c> would otherwise have pulled directly.
    /// </para>
    /// <para>
    /// An absolute path, not one relative to the consumer's AppHost project directory - both
    /// <c>WithBindMount</c> and <c>WithDockerfile</c>'s context-path parameter resolve a
    /// relative path against the *consumer's* AppHost project directory, not this package's, so
    /// an absolute path sidesteps that regardless of which project calls <c>AddFlare</c> or from
    /// where.
    /// </para>
    /// </remarks>
    private static string WriteClickHouseInitDockerContext(IResource clickhouseResource)
    {
        const string ResourcePrefix = "Aspire.Hosting.Flare.ClickHouseInit.";

        if (!clickhouseResource.TryGetContainerImageName(out var baseImage))
        {
            throw new InvalidOperationException(
                $"Could not resolve {clickhouseResource.Name}'s container image to build a custom ClickHouse-init image FROM.");
        }

        var assembly = typeof(FlareResourceBuilderExtensions).Assembly;
        var contextDir = Path.Combine(Path.GetTempPath(), "flare-clickhouse-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contextDir);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[ResourcePrefix.Length..];
            using var resourceStream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was listed but could not be opened.");
            using var fileStream = File.Create(Path.Combine(contextDir, fileName));
            resourceStream.CopyTo(fileStream);
        }

        File.WriteAllText(
            Path.Combine(contextDir, "Dockerfile"),
            $"""
            FROM {baseImage}
            COPY *.sql /docker-entrypoint-initdb.d/
            """);

        return contextDir;
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

    /// <summary>
    /// Third-party image (not one of Flare's own published ones above) for the opt-in
    /// Docker-driven Resources page's socket-proxy sidecar - see
    /// <c>enableResourceGraph</c>'s doc comment on <see cref="FlareResourceBuilderExtensions.AddFlare"/>.
    /// No <c>imageTag</c> parameter reuse here (unlike the three above) - this isn't
    /// versioned in lockstep with Flare's own releases, so it always pulls <c>:latest</c>,
    /// same as <c>docker-compose.yml</c>'s own <c>docker-proxy</c> service.
    /// </summary>
    internal const string DockerProxyImage = "tecnativa/docker-socket-proxy";
}
