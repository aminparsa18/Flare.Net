using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.Kubernetes.Resources;
using Aspire.Hosting.Publishing;

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
    /// insert buffer), the OTLP ingest receiver, the query API, the alert-rule evaluation
    /// worker, and the dashboard SPA - wrapping Flare's published Docker Hub images. Mirrors
    /// the resource graph Flare's own <c>Flare.AppHost/Program.cs</c> wires up locally,
    /// swapping <c>AddProject</c> for <c>AddContainer</c>.
    /// </summary>
    /// <remarks>
    /// Only the dashboard shows up in the Aspire dashboard's resource list by default - the
    /// composite <see cref="FlareResource"/> and its five backing resources (ClickHouse, its
    /// database, Redis, and the ingest/api/alert-worker containers) are marked hidden, since
    /// they're implementation details a consumer adding Flare to their own AppHost doesn't need to see.
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
    /// <para>
    /// <b>⚠️ On Kubernetes, Flare's storage is ephemeral by default.</b> ClickHouse's, Redis's, and
    /// the identity database's <c>WithDataVolume()</c>/<c>WithVolume()</c> calls render as plain
    /// <c>emptyDir: {}</c> volumes in the generated <c>StatefulSet</c>/<c>Deployment</c> specs, not
    /// <c>PersistentVolumeClaim</c>s - registering <see cref="KubernetesEnvironmentResource"/> alone
    /// does NOT make Flare's logs or auth database durable. All historical telemetry and the
    /// identity/auth database are lost on the next pod reschedule unless you bind a real persistent
    /// volume to each of <c>{name}-clickhouse-data</c>, <c>{name}-redis-data</c>, and
    /// <c>{name}-identity-data</c> - use <see cref="WithPersistentStorage"/> to do that (this method
    /// still won't pick a storage class/capacity/access-mode policy on your behalf - you supply
    /// three already-configured <c>AddPersistentVolume</c> results, <see cref="WithPersistentStorage"/>
    /// just performs the binding). See <c>docs/aspire-hosting.md</c>'s "Kubernetes" section (the
    /// "ClickHouse/Redis/identity data does NOT survive a pod restart by default" bullet) for the
    /// full story and a worked example. Skipping this is also flagged as a console warning during
    /// <c>aspire publish</c>/<c>aspire deploy</c> against a Kubernetes target, so it isn't only
    /// discoverable by reading documentation.
    /// </para>
    /// </remarks>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the Flare resource group.</param>
    /// <param name="imageTag">
    /// The tag to pull for all four Flare images. Defaults to <c>"0.5.0"</c>, the latest
    /// stable Flare release this package version was tested against - deliberately NOT
    /// Docker Hub's floating <c>latest</c>/<c>edge</c> tags, so a given
    /// <c>Flare.Hosting.Aspire</c> NuGet version keeps pulling the same images forever
    /// instead of silently changing behavior as new Flare releases ship. This default is
    /// bumped as part of cutting each new <c>Flare.Hosting.Aspire</c> release, once that
    /// release has been tested against a newer Flare image - it does not track Docker
    /// Hub automatically. Pass <c>"edge"</c> yourself to track Flare's unreleased
    /// <c>main</c> branch instead. <see cref="WithIngestImage"/>/<see cref="WithApiImage"/>/
    /// <see cref="WithDashboardImage"/>/<see cref="WithAlertWorkerImage"/> reuse this same tag
    /// when overriding just an image name/registry - there's no separate per-image tag
    /// override.
    /// <para>
    /// <c>xracer007/flare-alert-worker</c> has been published on Docker Hub since the
    /// <c>v0.5.0</c> Flare release (<c>docs-internal/adr/0018-alert-worker-extraction.md</c>'s
    /// release gate) - any tag from <c>0.5.0</c> onward resolves for all four images,
    /// alert-worker included.
    /// </para>
    /// </param>
    /// <param name="enableResourceGraph">
    /// Turns on the dashboard's Resources page (a live topology graph) for this Flare
    /// instance. Off by default - real, meaningful cluster/Docker access is involved (see
    /// below), and this package follows the same "absent config = off" pattern the rest of
    /// this method uses rather than defaulting it on. Kept as a constructor-time argument
    /// rather than a <c>With*</c> chain method (unlike everything else this method used to
    /// take as a parameter) because it decides whether whole extra resources exist at all
    /// (an RBAC <c>ServiceAccount</c>/<c>Role</c>/<c>RoleBinding</c> on Kubernetes, or an
    /// entire docker-socket-proxy sidecar container on Docker) - conditionally creating or
    /// tearing those down after the fact would be far more invasive than reconfiguring a
    /// port or image on an already-created resource, which is all the <c>With*</c> methods
    /// below do. Which of the two topology providers this actually wires up is picked
    /// automatically from which compute environment is registered - see the "Opt-in
    /// Resources page" block inside this method for the exact branch - not a separate
    /// parameter, since a given AppHost only ever targets one deployment environment at a
    /// time:
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
    /// <returns>
    /// An <see cref="IResourceBuilder{FlareResource}"/> for the composite Flare resource. Chain
    /// <see cref="WithIngestGrpcPort"/>/<see cref="WithIngestHttpPort"/>/<see cref="WithApiPort"/>/
    /// <see cref="WithDashboardPort"/>, <see cref="WithIngestImage"/>/<see cref="WithApiImage"/>/
    /// <see cref="WithDashboardImage"/>/<see cref="WithAlertWorkerImage"/>, <see cref="WithApiKey"/>, and
    /// <see cref="WithPublicApiUrl"/>/<see cref="WithPublicDashboardUrl"/> off the result to
    /// configure everything this method used to take as extra parameters - the usual Aspire
    /// convention (compare <c>AddRedis(...).WithPersistence(...)</c>) rather than one long
    /// parameter list. Each of them returns the same <see cref="FlareResource"/> builder, so
    /// they chain freely and in any order:
    /// <code>
    /// var flare = builder.AddFlare("flare", enableResourceGraph: true)
    ///     .WithIngestGrpcPort(4327)
    ///     .WithApiKey(apiKeyParam)
    ///     .WithPublicApiUrl(publicApiUrlParam)
    ///     .WithPublicDashboardUrl(publicDashboardUrlParam);
    /// </code>
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="imageTag"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> AddFlare(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "flare",
        string imageTag = "0.5.0",
        bool enableResourceGraph = false)
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
        // ports - same reasoning as Flare.AppHost/Program.cs. Ports are left at their
        // conventional defaults here (port: null) - use WithIngestGrpcPort/WithIngestHttpPort
        // below to override; WithEndpoint's own "change an existing named endpoint" overload
        // (see those methods) reconfigures the "otlp-grpc"/"otlp-http" endpoints created here
        // without needing the port up front.
        var ingest = builder.AddContainer($"{name}-ingest", FlareContainerImageTags.IngestImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithVolume(identityVolumeName, "/data/identity")
            .WithEnvironment("Identity__DbPath", identityDbPath)
            .WithEndpoint(port: null, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
            .WithEndpoint(port: null, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
            .WithHttpHealthCheck("/health", endpointName: "otlp-http")
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("ingest", "clickhouse:Reference,redis:Reference");
        // "edge" is a mutable tag republished on every push to main - without this, Docker only
        // pulls it once (the default pull policy is "if missing locally") and then silently
        // reuses that stale local image on every future run, forever, with no error. This forces
        // a fresh registry check on every `aspire start` so consumers (including Flare's own
        // examples/ExampleApp.AppHost) actually get current bits. Unconditional here (unlike
        // before this type had With* chain methods) because AddFlare always starts every
        // container off the default Docker Hub image now - WithIngestImage/WithApiImage/
        // WithDashboardImage below reset this back to ImagePullPolicy.Default when a consumer
        // overrides to a local, registry-less image, for the same reason the old ingestImage/
        // apiImage/dashboardImage parameters gated this: ImagePullPolicy.Always against a
        // registry-less image would just fail the pull outright.
        ingest.WithImagePullPolicy(ImagePullPolicy.Always);

        // Attach ingest's real endpoints to the composite FlareResource so consumers can reach
        // them via `flare` itself - through `.WithReference(flare)` (ConnectionStringExpression
        // below) or the WithOtlpEndpoint helper - instead of hand-writing
        // "http://localhost:4317" and hoping local dev topology holds.
        flare.Resource.SetIngestEndpoints(ingest.GetEndpoint("otlp-grpc"), ingest.GetEndpoint("otlp-http"));

        // Flare.Api: the query API (search/filter/time-range/aggregate) and live-tail streaming
        // endpoint over the same clickhousedb.logs table Flare.Ingest writes to. A normal
        // proxied Aspire HTTP endpoint - callers go through Aspire's dev-proxy/service
        // discovery like any other resource.
        var api = builder.AddContainer($"{name}-api", FlareContainerImageTags.ApiImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            // Same volume name as ingest above - see that assignment's remarks.
            .WithVolume(identityVolumeName, "/data/identity")
            .WithEnvironment("Identity__DbPath", identityDbPath)
            .WithHttpEndpoint(port: null, targetPort: 8080)
            .WithHttpHealthCheck("/health")
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("api", "clickhouse:Reference,redis:Reference");
        // Same "edge" staleness reasoning and unconditional-then-reset-on-override story as
        // ingest above.
        api.WithImagePullPolicy(ImagePullPolicy.Always);

        // Flare.AlertWorker: periodic alert-rule evaluation, split out of Flare.Api into its
        // own process (docs-internal/adr/0018-alert-worker-extraction.md) so that restarting
        // `api` no longer also stops alert evaluation. Same ClickHouse/Redis references as
        // `api` - AlertQueryService/CompositeAlertNotifier are reused from Flare.Api via a
        // ProjectReference, not a new backing store. No identity volume/Cors - it never
        // touches either. No published ports (internal only, same as redis) - nothing outside
        // this stack talks to this process directly.
        var alertWorker = builder.AddContainer($"{name}-alert-worker", FlareContainerImageTags.AlertWorkerImage, imageTag)
            .WithReference(logsDb, connectionName: "clickhousedb")
            .WaitFor(logsDb)
            .WithReference(redis, connectionName: "redis")
            .WaitFor(redis)
            .WithHttpEndpoint(port: null, targetPort: 8080)
            .WithHttpHealthCheck("/health")
            .WithParentRelationship(flare)
            .WithHidden()
            .WithFlareResourceLabels("alert-worker", "clickhouse:Reference,redis:Reference");
        // Same "edge" staleness reasoning and unconditional-then-reset-on-override story as
        // ingest above.
        alertWorker.WithImagePullPolicy(ImagePullPolicy.Always);

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
            .WithHttpEndpoint(port: null, targetPort: 3000)
            // A real liveness signal - the container reaching "Running" doesn't mean SvelteKit's
            // Node server is actually accepting requests yet. WaitForFlare (below) waits on this,
            // not just the container's Running state.
            .WithHttpHealthCheck("/")
            .WithParentRelationship(flare)
            .WithFlareResourceLabels("dashboard", "api:Reference");
        // Same "edge" staleness reasoning and unconditional-then-reset-on-override story as
        // ingest above - this is the one that actually bit us: a consumer's Docker cache pins a
        // stale dashboard build indefinitely otherwise, with nothing on screen telling them why
        // they're not seeing recent dashboard changes.
        dashboard.WithImagePullPolicy(ImagePullPolicy.Always);
        // Defaults for what WithPublicApiUrl/WithPublicDashboardUrl below let a consumer override
        // once actually deployed rather than `aspire run` - see those methods' doc comments and
        // docs/aspire-hosting.md's "Publishing / deploying via aspire publish" section. Always set
        // here (unlike before this type had With* chain methods, when this branched on whether the
        // publicApiUrl/publicDashboardUrl parameters were passed) because AddFlare no longer knows
        // whether a consumer will chain an override afterward - WithEnvironment calls made later
        // by those methods simply win over these, the same "last registered callback for a given
        // key wins" behavior any other doubled-up WithEnvironment call relies on.
        dashboard.WithEnvironment("PUBLIC_API_URL", api.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));

        // Flare.Api rejects every browser origin by default once auth is in the picture
        // (Cors:AllowedOrigins has no safe default - see docs/auth.md in Flare's own
        // repo) - without this, every fetch the dashboard's browser-side JS makes fails
        // CORS and the app hangs on its own "checking session" spinner with no
        // indication why. Same LocalhostNetwork-pinned endpoint reference already used
        // for PUBLIC_API_URL above and for the same reason: this has to resolve to what
        // the *browser* sees, not container-network DNS. Confirmed live against a real
        // Aspire-orchestrated run that this was missing and broke the dashboard outright.
        dashboard.WithEnvironment("ORIGIN", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));
        api.WithEnvironment("Cors__AllowedOrigins__0", dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));

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

        // Stash the ingest/api/dashboard sub-resources' Aspire resource names (plus the shared
        // imageTag) so the With* chain methods below - and WaitForFlare - can reach back into
        // them after this method has already returned, without the caller needing to hold onto
        // their own reference to any of them.
        flare.Resource.SetIngestResourceName(ingest.Resource.Name);
        flare.Resource.SetApiResourceName(api.Resource.Name);
        flare.Resource.SetAlertWorkerResourceName(alertWorker.Resource.Name);
        flare.Resource.SetDashboardResourceName(dashboard.Resource.Name);
        flare.Resource.SetClickHouseResourceName(clickhouse.Resource.Name);
        flare.Resource.SetRedisResourceName(redis.Resource.Name);
        flare.Resource.SetImageTag(imageTag);

        // Deferred, not called inline here: a consumer's own WithPersistentStorage(...) call (if
        // any) only runs *after* AddFlare returns, as the next link in the same chain
        // (`builder.AddFlare(...).WithPersistentStorage(...)`) - checking
        // flare.Resource.PersistentStorageConfigured synchronously at this point, before that chain
        // call has had a chance to run, would see it as never configured and warn every time
        // regardless of whether the consumer actually fixed it. BeforePublishEvent fires once the
        // whole application model - every With* chain call included - is fully built, right before
        // aspire publish/aspire deploy's own publishing pipeline runs, which is exactly the "has the
        // consumer's whole AddFlare(...).With*(...) chain finished running yet" checkpoint this
        // needs. Not yet confirmed live that BeforePublishEvent fires identically for `aspire
        // deploy`, not just `aspire publish` - same "needs its own live e2e pass" gap
        // docs-internal/planning/roadmap.md already flags for this feature as a whole.
        builder.Eventing.Subscribe<BeforePublishEvent>((_, _) =>
        {
            WarnIfKubernetesStorageIsEphemeral(builder, name, flare.Resource);
            return Task.CompletedTask;
        });

        return flare;
    }

    /// <summary>Resolves <paramref name="flare"/>'s ingest sub-resource, for the <c>With*</c> chain methods below.</summary>
    private static IResourceBuilder<ContainerResource> GetIngestBuilder(IResourceBuilder<FlareResource> flare) =>
        flare.ApplicationBuilder.CreateResourceBuilder<ContainerResource>(flare.Resource.IngestResourceName);

    /// <summary>Resolves <paramref name="flare"/>'s api sub-resource, for the <c>With*</c> chain methods below.</summary>
    private static IResourceBuilder<ContainerResource> GetApiBuilder(IResourceBuilder<FlareResource> flare) =>
        flare.ApplicationBuilder.CreateResourceBuilder<ContainerResource>(flare.Resource.ApiResourceName);

    /// <summary>Resolves <paramref name="flare"/>'s alert-worker sub-resource, for <see cref="WithAlertWorkerImage"/>.</summary>
    private static IResourceBuilder<ContainerResource> GetAlertWorkerBuilder(IResourceBuilder<FlareResource> flare) =>
        flare.ApplicationBuilder.CreateResourceBuilder<ContainerResource>(flare.Resource.AlertWorkerResourceName);

    /// <summary>Resolves <paramref name="flare"/>'s dashboard sub-resource, for <see cref="WaitForFlare{TDestination}"/> and the <c>With*</c> chain methods below.</summary>
    private static IResourceBuilder<ContainerResource> GetDashboardBuilder(IResourceBuilder<FlareResource> flare) =>
        flare.ApplicationBuilder.CreateResourceBuilder<ContainerResource>(flare.Resource.DashboardResourceName);

    /// <summary>Resolves <paramref name="flare"/>'s ClickHouse sub-resource, for <see cref="WithPersistentStorage"/>.</summary>
    private static IResourceBuilder<ClickHouseServerResource> GetClickHouseBuilder(IResourceBuilder<FlareResource> flare) =>
        flare.ApplicationBuilder.CreateResourceBuilder<ClickHouseServerResource>(flare.Resource.ClickHouseResourceName);

    /// <summary>Resolves <paramref name="flare"/>'s Redis sub-resource, for <see cref="WithPersistentStorage"/>.</summary>
    private static IResourceBuilder<RedisResource> GetRedisBuilder(IResourceBuilder<FlareResource> flare) =>
        flare.ApplicationBuilder.CreateResourceBuilder<RedisResource>(flare.Resource.RedisResourceName);

    /// <summary>
    /// Overrides the OTLP gRPC endpoint's host port (default: the conventional 4317, unproxied -
    /// see <see cref="AddFlare"/>'s ingest remarks). Reconfigures the existing "otlp-grpc" named
    /// endpoint <see cref="AddFlare"/> already created, rather than creating a new one -
    /// <c>createIfNotExists: false</c> below fails loudly instead of silently creating a
    /// wrong-shaped endpoint if that assumption ever stops holding.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="port">The host port.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithIngestGrpcPort(this IResourceBuilder<FlareResource> flare, int port)
    {
        ArgumentNullException.ThrowIfNull(flare);

        GetIngestBuilder(flare).WithEndpoint("otlp-grpc", e => e.Port = port, createIfNotExists: false);
        return flare;
    }

    /// <summary>
    /// Overrides the OTLP HTTP endpoint's host port (default: the conventional 4318, unproxied -
    /// see <see cref="AddFlare"/>'s ingest remarks). Same reconfigure-by-name mechanism as
    /// <see cref="WithIngestGrpcPort"/>, targeting the "otlp-http" named endpoint instead.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="port">The host port.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithIngestHttpPort(this IResourceBuilder<FlareResource> flare, int port)
    {
        ArgumentNullException.ThrowIfNull(flare);

        GetIngestBuilder(flare).WithEndpoint("otlp-http", e => e.Port = port, createIfNotExists: false);
        return flare;
    }

    /// <summary>
    /// Overrides Flare's query API's host port (a normal proxied Aspire HTTP endpoint, dynamically
    /// assigned by default). Reconfigures the existing "http" named endpoint <see cref="AddFlare"/>
    /// already created via <c>WithHttpEndpoint</c>, the same reconfigure-by-name mechanism as
    /// <see cref="WithIngestGrpcPort"/>.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="port">The host port.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithApiPort(this IResourceBuilder<FlareResource> flare, int port)
    {
        ArgumentNullException.ThrowIfNull(flare);

        GetApiBuilder(flare).WithEndpoint("http", e => e.Port = port, createIfNotExists: false);
        return flare;
    }

    /// <summary>
    /// Overrides the dashboard SPA's host port (a normal proxied Aspire HTTP endpoint, dynamically
    /// assigned by default). Same reconfigure-by-name mechanism as <see cref="WithApiPort"/>.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="port">The host port.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithDashboardPort(this IResourceBuilder<FlareResource> flare, int port)
    {
        ArgumentNullException.ThrowIfNull(flare);

        GetDashboardBuilder(flare).WithEndpoint("http", e => e.Port = port, createIfNotExists: false);
        return flare;
    }

    /// <summary>
    /// Overrides the ingest image name (registry/repo, no tag - <see cref="AddFlare"/>'s
    /// <c>imageTag</c> still supplies the tag, reused automatically). Local-dev escape hatch for
    /// pointing at an image built with <c>docker compose build</c> instead of Docker Hub, e.g.
    /// <c>"flarenet-ingest"</c> with <c>imageTag: "latest"</c> passed to <see cref="AddFlare"/> -
    /// Docker won't re-pull a mutable tag like <c>edge</c> that's already cached locally, so this
    /// is how to force local source into an AppHost run without waiting on a fresh Docker Hub
    /// publish. Also resets the ingest container's pull policy from <see cref="AddFlare"/>'s
    /// default <see cref="ImagePullPolicy.Always"/> back to <see cref="ImagePullPolicy.Default"/> -
    /// <c>Always</c> against a registry-less local image would just fail the pull outright.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="image">The image name (registry/repo, no tag).</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> WithIngestImage(this IResourceBuilder<FlareResource> flare, string image)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentException.ThrowIfNullOrEmpty(image);

        GetIngestBuilder(flare)
            .WithImage(image, flare.Resource.ImageTag)
            .WithImagePullPolicy(ImagePullPolicy.Default);
        return flare;
    }

    /// <summary>Same override as <see cref="WithIngestImage"/>, for the api image.</summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="image">The image name (registry/repo, no tag).</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> WithApiImage(this IResourceBuilder<FlareResource> flare, string image)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentException.ThrowIfNullOrEmpty(image);

        GetApiBuilder(flare)
            .WithImage(image, flare.Resource.ImageTag)
            .WithImagePullPolicy(ImagePullPolicy.Default);
        return flare;
    }

    /// <summary>Same override as <see cref="WithIngestImage"/>, for the dashboard image.</summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="image">The image name (registry/repo, no tag).</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> WithDashboardImage(this IResourceBuilder<FlareResource> flare, string image)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentException.ThrowIfNullOrEmpty(image);

        GetDashboardBuilder(flare)
            .WithImage(image, flare.Resource.ImageTag)
            .WithImagePullPolicy(ImagePullPolicy.Default);
        return flare;
    }

    /// <summary>Same override as <see cref="WithIngestImage"/>, for the alert-worker image.</summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="image">The image name (registry/repo, no tag).</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null or empty.</exception>
    public static IResourceBuilder<FlareResource> WithAlertWorkerImage(this IResourceBuilder<FlareResource> flare, string image)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentException.ThrowIfNullOrEmpty(image);

        GetAlertWorkerBuilder(flare)
            .WithImage(image, flare.Resource.ImageTag)
            .WithImagePullPolicy(ImagePullPolicy.Default);
        return flare;
    }

    /// <summary>
    /// Requires OTLP callers to present an ingest API key - sets <c>ingest</c>'s
    /// <c>Auth__IngestKeyRequired=true</c> and <c>Auth__StaticIngestApiKey</c> to
    /// <paramref name="apiKey"/>'s value (see
    /// <c>Flare.Identity.Auth.IngestAuthOptions.StaticIngestApiKey</c>'s remarks for why this is a
    /// separate, config-driven mechanism from the dashboard's "create a key" flow) - config-driven
    /// rather than "create a key via the dashboard," since that manual flow doesn't fit an
    /// automated resource-graph-wiring use case like this one (Planning.md's "Auth + multi-user /
    /// roles" item, ingest-side half). Not called at all (the default), ingest stays anonymous,
    /// matching today's Flare.Ingest default. Only wired onto <c>ingest</c> - <c>api</c>'s own auth
    /// (dashboard user sessions) is unrelated to this key. A consuming app's own
    /// <c>AddFlareOtlpExporter</c> call (from the <c>Aspire.Flare</c> package) needs the same raw
    /// value passed to its own <c>configureSettings: s =&gt; s.ApiKey = ...</c> delegate - there's
    /// no automatic flow-through from this method yet, see <c>FlareSettings.ApiKey</c>'s remarks.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="apiKey">A <c>secret: true</c> <c>AddParameter</c> result.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithApiKey(this IResourceBuilder<FlareResource> flare, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentNullException.ThrowIfNull(apiKey);

        GetIngestBuilder(flare)
            .WithEnvironment("Auth__IngestKeyRequired", "true")
            .WithEnvironment("Auth__StaticIngestApiKey", apiKey);
        return flare;
    }

    /// <summary>
    /// Overrides the externally-reachable URL browsers should use to reach <c>api</c>, surfaced to
    /// the dashboard as <c>PUBLIC_API_URL</c>. Not called (the default), this stays pinned to
    /// <c>api</c>'s own loopback endpoint - correct for <c>aspire run</c>, where the dashboard and
    /// the browser viewing it are on the same machine. Once actually publishing/deploying
    /// (<c>aspire publish</c>/<c>aspire deploy</c> - see <c>docs/aspire-hosting.md</c>'s
    /// "Publishing / deploying via aspire publish" section) that assumption stops holding - the
    /// browser reaches the deployed stack by a real hostname/IP, not <c>localhost</c> - so pass a
    /// <c>secret: false</c> <c>AddParameter</c> result here (left unset, so Aspire captures it as
    /// an <c>.env.{environment}</c> placeholder an operator fills in with the real deployed URL per
    /// environment) instead. Overrides <see cref="AddFlare"/>'s loopback default by registering a
    /// second, later <c>WithEnvironment("PUBLIC_API_URL", ...)</c> call - the later one wins.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="publicApiUrl">A <c>secret: false</c> <c>AddParameter</c> result.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithPublicApiUrl(this IResourceBuilder<FlareResource> flare, IResourceBuilder<ParameterResource> publicApiUrl)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentNullException.ThrowIfNull(publicApiUrl);

        GetDashboardBuilder(flare).WithEnvironment("PUBLIC_API_URL", publicApiUrl);
        return flare;
    }

    /// <summary>
    /// Overrides the externally-reachable URL browsers should use to reach the dashboard itself,
    /// surfaced as the dashboard's own <c>ORIGIN</c> (required by SvelteKit's Node adapter to
    /// accept requests) and as <c>api</c>'s <c>Cors__AllowedOrigins__0</c> (so <c>api</c> accepts
    /// browser requests originating from it). Same default/override story as
    /// <see cref="WithPublicApiUrl"/> - not called, keeps today's loopback-pinned <c>aspire run</c>
    /// behavior; call it with an <c>AddParameter</c> result for publish/deploy.
    /// </summary>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="publicDashboardUrl">A <c>secret: false</c> <c>AddParameter</c> result.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
    public static IResourceBuilder<FlareResource> WithPublicDashboardUrl(this IResourceBuilder<FlareResource> flare, IResourceBuilder<ParameterResource> publicDashboardUrl)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentNullException.ThrowIfNull(publicDashboardUrl);

        GetDashboardBuilder(flare).WithEnvironment("ORIGIN", publicDashboardUrl);
        GetApiBuilder(flare).WithEnvironment("Cors__AllowedOrigins__0", publicDashboardUrl);
        return flare;
    }

    /// <summary>
    /// Binds consumer-supplied Kubernetes persistent volumes to ClickHouse's, Redis's, and the
    /// identity database's storage - the first-class replacement for hand-wiring
    /// <c>AddPersistentVolume</c>/<c>WithPersistentVolume</c> onto the sub-resources yourself, which
    /// used to be the only option (see <c>docs/aspire-hosting.md</c>'s "Kubernetes" section and
    /// <see cref="WarnIfKubernetesStorageIsEphemeral"/>'s remarks for the durability problem this
    /// solves).
    /// </summary>
    /// <remarks>
    /// Each parameter is the result of <c>kubernetesEnvironment.AddPersistentVolume(...)</c>
    /// (<c>Aspire.Hosting.Kubernetes</c> <c>13.5.3-preview.1.26425.3</c>+ - see
    /// <c>Directory.Packages.props</c>'s remarks on that package) plus whatever
    /// <c>WithStorageClass</c>/<c>WithCapacity</c>/<c>WithAccessMode</c> chain the consumer needs -
    /// this method still deliberately does not pick a storage class/capacity/access-mode policy on
    /// the consumer's behalf (<see cref="AddFlare"/> never could, for the same reason), only the
    /// <em>binding</em> of an already-configured volume to the right sub-resource:
    /// <code>
    /// var k8s = builder.AddKubernetesEnvironment("k8s");
    /// var flare = builder.AddFlare("flare")
    ///     .WithPersistentStorage(
    ///         clickHouseVolume: k8s.AddPersistentVolume("flare-clickhouse-data")
    ///             .WithStorageClass("standard").WithCapacity("20Gi")
    ///             .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
    ///         redisVolume: k8s.AddPersistentVolume("flare-redis-data")
    ///             .WithStorageClass("standard").WithCapacity("5Gi")
    ///             .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
    ///         identityVolume: k8s.AddPersistentVolume("flare-identity-data")
    ///             .WithStorageClass("standard").WithCapacity("1Gi")
    ///             .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce));
    /// </code>
    /// <para>
    /// Binds by name, not by an explicit mount path - each of <paramref name="clickHouseVolume"/>/
    /// <paramref name="redisVolume"/>/<paramref name="identityVolume"/> is matched against the
    /// existing <c>WithDataVolume</c> (ClickHouse/Redis) / <c>WithVolume</c> (identity) call
    /// <see cref="AddFlare"/> already made under the volume names <c>{name}-clickhouse-data</c>/
    /// <c>{name}-redis-data</c>/<c>{name}-identity-data</c>. Per
    /// <see href="https://aspire.dev/deployment/kubernetes/persistent-volumes/">Aspire's own
    /// documentation</see>, the parameterless <c>WithPersistentVolume(volume)</c> overload used here
    /// requires that name match rather than taking an explicit mount path itself (unlike the
    /// <c>WithPersistentVolume(volume, mountPath)</c> overload, for a resource with no volume mount
    /// of its own) - the volume's own Aspire *resource* name (the string passed to
    /// <c>AddPersistentVolume</c> itself, e.g. <c>"flare-clickhouse-data"</c> above) does not need to
    /// match anything; only the data-volume name it ends up bound to does, and that binding is what
    /// this method performs.
    /// </para>
    /// <para>
    /// <b>The identity volume is bound twice</b> - once each to the ingest and api sub-resources -
    /// because <see cref="AddFlare"/>'s identity database is a single SQLite file shared between
    /// both containers via one named volume (see <see cref="AddFlare"/>'s <c>identityVolumeName</c>
    /// remarks). Both are promoted to a <c>StatefulSet</c> as a result - Aspire's unconditional
    /// behavior for any workload bound to a persistent volume, no opt-out. Whether one
    /// <c>ReadWriteOnce</c> PVC can actually satisfy two separate StatefulSets' Pods at once depends
    /// on the storage class/CSI driver (most block-storage classes are node-scoped and would need
    /// <c>ReadWriteMany</c>, or both Pods landing on the same node) - exactly the kind of detail this
    /// feature's own live e2e pass (<c>docs-internal/planning/roadmap.md</c>) needs to confirm
    /// against a real cluster; not yet verified live as of this method's introduction.
    /// </para>
    /// <para>
    /// Requires suppressing <c>ASPIRECOMPUTE002</c> in the consumer's own AppHost project - the same
    /// requirement <c>AddPersistentVolume</c>/<c>WithStorageClass</c>/<c>WithCapacity</c>/
    /// <c>WithAccessMode</c> already carry to build the arguments passed in here. This method is
    /// marked <see cref="ExperimentalAttribute"/> with that same diagnostic ID rather than minting a
    /// separate one of its own, since it's a thin binding layer over an Aspire API that is itself
    /// still experimental, not a new experimental surface in its own right.
    /// </para>
    /// </remarks>
    /// <param name="flare">The Flare resource returned by <see cref="AddFlare"/>.</param>
    /// <param name="clickHouseVolume">A <c>kubernetesEnvironment.AddPersistentVolume(...)</c> result, bound to ClickHouse's <c>{name}-clickhouse-data</c> volume.</param>
    /// <param name="redisVolume">A <c>kubernetesEnvironment.AddPersistentVolume(...)</c> result, bound to Redis's <c>{name}-redis-data</c> volume.</param>
    /// <param name="identityVolume">A <c>kubernetesEnvironment.AddPersistentVolume(...)</c> result, bound to the identity database's <c>{name}-identity-data</c> volume on both the ingest and api sub-resources.</param>
    /// <returns><paramref name="flare"/>, for chaining.</returns>
#pragma warning disable ASPIRECOMPUTE002 // Aspire.Hosting.Kubernetes' persistent-volume APIs are still evaluation-only - see this method's own remarks.
    [Experimental("ASPIRECOMPUTE002")]
    public static IResourceBuilder<FlareResource> WithPersistentStorage(
        this IResourceBuilder<FlareResource> flare,
        IResourceBuilder<KubernetesPersistentVolumeResource> clickHouseVolume,
        IResourceBuilder<KubernetesPersistentVolumeResource> redisVolume,
        IResourceBuilder<KubernetesPersistentVolumeResource> identityVolume)
    {
        ArgumentNullException.ThrowIfNull(flare);
        ArgumentNullException.ThrowIfNull(clickHouseVolume);
        ArgumentNullException.ThrowIfNull(redisVolume);
        ArgumentNullException.ThrowIfNull(identityVolume);

        GetClickHouseBuilder(flare).WithPersistentVolume(clickHouseVolume);
        GetRedisBuilder(flare).WithPersistentVolume(redisVolume);
        GetIngestBuilder(flare).WithPersistentVolume(identityVolume);
        GetApiBuilder(flare).WithPersistentVolume(identityVolume);

        flare.Resource.MarkPersistentStorageConfigured();
        return flare;
    }
#pragma warning restore ASPIRECOMPUTE002

    /// <summary>
    /// Prints a console warning during <c>aspire publish</c>/<c>aspire deploy</c> when this
    /// <see cref="AddFlare"/> call is publishing against a registered
    /// <see cref="KubernetesEnvironmentResource"/> and <see cref="WithPersistentStorage"/> was never
    /// called - see <see cref="AddFlare"/>'s remarks for the full "why" (the review this addresses:
    /// registering <c>AddKubernetesEnvironment</c>+<c>AddFlare</c> alone reasonably looks like "Flare
    /// is deployed, my telemetry is durable," which is false until persistent volumes are wired up).
    /// </summary>
    /// <remarks>
    /// Deliberately unconditional whenever a Kubernetes target is being published to, not gated on
    /// <c>enableResourceGraph</c> or any other opt-in - the ephemeral-storage risk exists regardless
    /// of whether the Resources page is enabled. Checking
    /// <paramref name="flareResource"/>.<see cref="FlareResource.PersistentStorageConfigured"/>
    /// rather than inspecting the application model for actual Kubernetes persistent-volume bindings
    /// is deliberate too - <c>WithPersistentVolume</c>'s own binding annotation type is internal to
    /// <c>Aspire.Hosting.Kubernetes</c>, so this package has no public API to detect it, only its own
    /// record of whether <see cref="WithPersistentStorage"/> ran. A consumer who binds
    /// <c>AddPersistentVolume</c>/<c>WithPersistentVolume</c> directly onto the sub-resources instead
    /// of calling <see cref="WithPersistentStorage"/> still sees this warning even though their
    /// storage is, in fact, durable - a known false positive in that one specific case, traded off
    /// against never risking the opposite false "you're covered" negative.
    /// <para>
    /// Called from a <c>BeforePublishEvent</c> subscription registered inside <see cref="AddFlare"/>
    /// (see that call site's remarks for why the check has to be deferred, rather than run inline
    /// during <see cref="AddFlare"/> itself), not written before this feature existed, when
    /// <see cref="AddFlare"/> was still the only place this warning could execute from and had
    /// nothing to defer past. Still written straight to the console (not through
    /// <c>ILogger</c>/DI) - <c>aspire publish</c>/<c>aspire deploy</c> both stream the AppHost
    /// process's own stdout/stderr straight to the terminal, so this reaches the operator running
    /// the command without needing any Aspire-version-specific publish-pipeline reporter API.
    /// </para>
    /// </remarks>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/> passed to <see cref="AddFlare"/>.</param>
    /// <param name="name">The Flare resource group's name, to name the three volumes in the message.</param>
    /// <param name="flareResource">The <see cref="FlareResource"/> <see cref="AddFlare"/> created, to check <see cref="FlareResource.PersistentStorageConfigured"/>.</param>
    private static void WarnIfKubernetesStorageIsEphemeral(IDistributedApplicationBuilder builder, string name, FlareResource flareResource)
    {
        if (!builder.ExecutionContext.IsPublishMode
            || !builder.Resources.OfType<KubernetesEnvironmentResource>().Any()
            || flareResource.PersistentStorageConfigured)
        {
            return;
        }

        Console.Error.WriteLine(
            $"""

            ⚠️  Flare ('{name}') storage is EPHEMERAL on Kubernetes unless you configure persistent volumes.
                ClickHouse, Redis, and the identity/auth database render as empty `emptyDir` volumes by
                default - all historical logs and the identity database are lost on the next pod
                reschedule, not just a full redeploy. Call .WithPersistentStorage(...) on this AddFlare(...)
                result with three kubernetesEnvironment.AddPersistentVolume(...) results (ClickHouse, Redis,
                identity) before relying on this for anything beyond a disposable smoke test. See
                docs/aspire-hosting.md's "Kubernetes" section for a worked example.

            """);
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

        return builder.WaitFor(GetDashboardBuilder(flare));
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
/// Docker Hub image coordinates for Flare's four published components (see
/// <c>.github/workflows/docker-publish.yml</c> in Flare's own repo). Unqualified Docker Hub
/// image names - registry defaults to docker.io.
/// </summary>
internal static class FlareContainerImageTags
{
    internal const string IngestImage = "xracer007/flare-ingest";
    internal const string ApiImage = "xracer007/flare-api";
    internal const string DashboardImage = "xracer007/flare-dashboard";
    internal const string AlertWorkerImage = "xracer007/flare-alert-worker";

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
