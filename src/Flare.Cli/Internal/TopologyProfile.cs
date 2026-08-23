namespace Flare.Cli.Internal;

/// <summary>
/// Which stack shape a Flare instance runs - decided once at its first `flare start`
/// (see <see cref="StateMetadata.Topology"/>) and never changed afterward (not a live
/// migration path, same posture docs/clustering.md already states for the repo-level
/// cluster compose file).
/// </summary>
internal enum FlareTopology
{
    Standalone,
    Cluster,
}

/// <summary>
/// Single source of truth for everything that differs per <see cref="FlareTopology"/> -
/// which services exist, which ports they publish, which ones this tool waits healthy
/// vs just running, which one `flare doctor`'s row-count check execs into, which ones
/// `flare update` diffs, and what `flare destroy`/`flare instances list` display.
/// Generalizes the same "previously duplicated N ways" problem
/// <see cref="PortDefaults"/>'s own doc comment already fixed once, for ports
/// specifically - cluster mode adding a topology axis is what forced fixing it properly
/// this time instead of adding yet another hardcoded per-command list.
/// </summary>
internal sealed class TopologyProfile
{
    public required FlareTopology Topology { get; init; }

    /// <summary>Logical embedded-resource name of this topology's compose template - see Flare.Cli.csproj.</summary>
    public required string ComposeResourceName { get; init; }

    /// <summary>Logical embedded-resource name of this topology's .env template - see Flare.Cli.csproj.</summary>
    public required string EnvTemplateResourceName { get; init; }

    /// <summary>The FLARE_IMAGE_TAG this topology's own .env template defaults to - fed into <see cref="StateMetadata.ImageTag"/> at first init so it starts in sync rather than hardcoded to Standalone's own pin (see the .env templates themselves for why these two differ today).</summary>
    public required string DefaultImageTag { get; init; }

    /// <summary>Host ports this topology's compose template actually publishes. Feeds <see cref="EnvGenerator"/>, <see cref="DoctorChecks.CheckPortsAvailable"/>, and named-instance auto-probing.</summary>
    public required (string Label, string EnvKey, int Fallback)[] Ports { get; init; }

    /// <summary>Services polled via `docker compose ps --format {{.Health}}` during `flare start`, in the same order as the compose file's own `depends_on` chain.</summary>
    public required string[] HealthCheckedServices { get; init; }

    /// <summary>Services with no compose-level healthcheck - polled for "running" instead of "healthy".</summary>
    public required string[] RunningOnlyServices { get; init; }

    /// <summary>Every service `flare status` renders a row for, paired with a `{ENV_KEY}`-templated port label (or a literal like "(internal only)").</summary>
    public required (string Service, string PortLabelTemplate)[] StatusRows { get; init; }

    /// <summary>Which service `flare doctor`'s ClickHouse row-count check execs `clickhouse-client` into. Any node works under `Distributed` tables in cluster mode - `clickhouse-1` is simplest and always present.</summary>
    public required string ClickHouseExecTarget { get; init; }

    /// <summary>Services `flare update` pulls and diffs image ids for - the `FLARE_IMAGE_TAG`-pinned app images only, same exclusion of ClickHouse/Keeper/Redis (which float on their own upstream tags) both topologies already have.</summary>
    public required string[] PullDiffServices { get; init; }

    /// <summary>Display string `flare destroy` prints after `down -v`, naming the named volumes actually removed.</summary>
    public required string DestroyVolumesLabel { get; init; }

    /// <summary>Short label for `flare instances list`'s Mode column.</summary>
    public required string DisplayLabel { get; init; }

    private static readonly TopologyProfile StandaloneProfile = new()
    {
        Topology = FlareTopology.Standalone,
        ComposeResourceName = "Flare.Cli.Templates.docker-compose.flare.yml",
        EnvTemplateResourceName = "Flare.Cli.Templates.env.template",
        DefaultImageTag = "0.2.0",
        Ports = PortDefaults.All,
        HealthCheckedServices = ["clickhouse", "redis", "ingest", "api"],
        RunningOnlyServices = ["dashboard"],
        StatusRows =
        [
            ("clickhouse", "localhost:{CLICKHOUSE_HTTP_PORT}"),
            ("redis", "(internal only)"),
            ("ingest", "localhost:{FLARE_INGEST_GRPC_PORT} grpc / {FLARE_INGEST_HTTP_PORT} http"),
            ("api", "localhost:{FLARE_API_PORT}"),
            ("dashboard", "localhost:{FLARE_DASHBOARD_PORT}"),
        ],
        ClickHouseExecTarget = "clickhouse",
        PullDiffServices = ["ingest", "api", "dashboard"],
        DestroyVolumesLabel = "clickhouse-data, redis-data, identity-data",
        DisplayLabel = "standalone",
    };

    // Keeper/ClickHouse nodes/clickhouse-lb publish no host ports in either the CLI's
    // own cluster template or the repo-root docker-compose.cluster.yml it mirrors - only
    // the two ingest replicas, api, and dashboard do, so the port table (unlike the
    // service lists below) doesn't carry a "ClickHouse HTTP" entry the way Standalone's
    // does. Adding one anyway would make DoctorChecks.CheckPortsAvailable preflight-check
    // a port nothing in this topology actually binds.
    private static readonly TopologyProfile ClusterProfile = new()
    {
        Topology = FlareTopology.Cluster,
        ComposeResourceName = "Flare.Cli.Templates.docker-compose.cluster.flare.yml",
        EnvTemplateResourceName = "Flare.Cli.Templates.env.cluster.template",
        // 0.3.0 - the first stable Flare release with cluster-mode support (v0.2.0
        // predates it). Deliberately its own pin, independent of Standalone's own
        // default. See env.cluster.template's own remarks.
        DefaultImageTag = "0.3.0",
        Ports =
        [
            ("Ingest gRPC (OTLP)", "FLARE_INGEST_GRPC_PORT", 4317),
            ("Ingest HTTP (OTLP)", "FLARE_INGEST_HTTP_PORT", 4318),
            ("Ingest-2 gRPC (OTLP)", "FLARE_INGEST2_GRPC_PORT", 4319),
            ("Ingest-2 HTTP (OTLP)", "FLARE_INGEST2_HTTP_PORT", 4320),
            ("API", "FLARE_API_PORT", 8080),
            ("Dashboard", "FLARE_DASHBOARD_PORT", 7777),
        ],
        HealthCheckedServices =
        [
            "keeper-1", "keeper-2", "keeper-3",
            "clickhouse-1", "clickhouse-2", "clickhouse-3", "clickhouse-4",
            "clickhouse-lb", "redis", "ingest-1", "ingest-2", "api",
        ],
        RunningOnlyServices = ["dashboard"],
        StatusRows =
        [
            ("keeper-1", "(internal only)"),
            ("keeper-2", "(internal only)"),
            ("keeper-3", "(internal only)"),
            ("clickhouse-1", "(internal only)"),
            ("clickhouse-2", "(internal only)"),
            ("clickhouse-3", "(internal only)"),
            ("clickhouse-4", "(internal only)"),
            ("clickhouse-lb", "(internal only)"),
            ("redis", "(internal only)"),
            ("ingest-1", "localhost:{FLARE_INGEST_GRPC_PORT} grpc / {FLARE_INGEST_HTTP_PORT} http"),
            ("ingest-2", "localhost:{FLARE_INGEST2_GRPC_PORT} grpc / {FLARE_INGEST2_HTTP_PORT} http"),
            ("api", "localhost:{FLARE_API_PORT}"),
            ("dashboard", "localhost:{FLARE_DASHBOARD_PORT}"),
        ],
        ClickHouseExecTarget = "clickhouse-1",
        PullDiffServices = ["ingest-1", "ingest-2", "api", "dashboard"],
        DestroyVolumesLabel = "keeper-{1,2,3}-data, clickhouse-{1,2,3,4}-data, redis-data, identity-data",
        DisplayLabel = "cluster",
    };

    public static TopologyProfile For(FlareTopology topology) => topology switch
    {
        FlareTopology.Standalone => StandaloneProfile,
        FlareTopology.Cluster => ClusterProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, null),
    };

    public static TopologyProfile Parse(string topology) => topology switch
    {
        "standalone" => StandaloneProfile,
        "cluster" => ClusterProfile,
        // Unrecognized value (e.g. a future CLI version's topology read by an older one)
        // - fall back to Standalone rather than throw, same "don't hard-fail on unknown
        // state.json content" posture StateMetadata.Load already takes.
        _ => StandaloneProfile,
    };
}
