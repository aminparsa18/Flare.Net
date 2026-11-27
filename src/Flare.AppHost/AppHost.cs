var builder = DistributedApplication.CreateBuilder(args);

// ClickHouse: log storage. `logsDb`'s connection string (the default user) goes only to the
// clickhouse-migrate job below; the services connect as their own users (see db/clickhouse/README.md).
//
// The `Aspire.Hosting.ClickHouse` resource has no ClickHouse-specific init-script hook
// (no WithInitFiles/WithInitBindMount, unlike e.g. the Postgres integration - confirmed
// against its docs/source, not assumed). Instead this uses the generic
// ContainerResourceBuilderExtensions.WithBindMount(source, target, isReadOnly) - any
// Aspire container resource - to mount db/clickhouse/ at
// /docker-entrypoint-initdb.d, the ClickHouse *official* Docker image's own
// (Aspire-independent) convention for running *.sql files once, in filename order, the
// first time the container starts against an empty data directory.
// Pin a fixed, shell-safe password instead of leaving it to AddClickHouse's default
// random-password parameter - confirmed live that a random password containing '-'/')'/
// '{'/'}' makes the official image's docker-entrypoint-initdb.d step fail outright
// (`clickhouse-client ... Code: 552: Unrecognized option '-2B.GBAsjV8)hC_tWe{JNW'`),
// because that script passes the password straight through to clickhouse-client's CLI
// arg parser with no escaping for values that look like flags. Same default "flare"
// docker-compose.yml already uses, for the same reason.
var clickhousePassword = builder.AddParameter("clickhouse-password", "flare", secret: true);
var clickhouse = builder.AddClickHouse("clickhouse", password: clickhousePassword)
    .WithDataVolume()
    .WithBindMount("../../db/clickhouse", "/docker-entrypoint-initdb.d", isReadOnly: true);
var logsDb = clickhouse.AddDatabase("clickhousedb");

// Least-privilege users (ADR-0282): db/clickhouse/config/flare-users.xml declares flare_ingest, flare_query
// and flare_app with passwords from these env vars. `clickhousedb` above (default) is the DDL credential,
// referenced only by the clickhouse-migrate job below (ADR-0284); each service connects as its own user
// through ConnectionStrings:clickhousedb-*.
// Fixed defaults like clickhouse-password; an unset FLARE_CH_* would be an empty password.
var chIngestPassword = builder.AddParameter("clickhouse-ingest-password", "flare", secret: true);
var chQueryPassword = builder.AddParameter("clickhouse-query-password", "flare", secret: true);
var chAppPassword = builder.AddParameter("clickhouse-app-password", "flare", secret: true);
clickhouse
    .WithBindMount("../../db/clickhouse/config/flare-users.xml", "/etc/clickhouse-server/users.d/flare-users.xml", isReadOnly: true)
    .WithEnvironment("FLARE_CH_INGEST_PASSWORD", chIngestPassword)
    .WithEnvironment("FLARE_CH_QUERY_PASSWORD", chQueryPassword)
    .WithEnvironment("FLARE_CH_APP_PASSWORD", chAppPassword);
ReferenceExpression ClickHouseUser(string user, IResourceBuilder<ParameterResource> password) =>
    ReferenceExpression.Create(
        $"Host={clickhouse.Resource.Host};Port={clickhouse.Resource.Port};Database=clickhousedb;Username={user};Password={password}");

// Opt-in cold tier for retention (docs-internal/adr/0144-cold-storage-rustfs.md): run with
// `Flare:ColdStorage=true` (e.g. `dotnet run --project src/Flare.AppHost -- --Flare:ColdStorage=true`)
// to add RustFS, a one-shot job that creates its bucket, and mount cold-storage.xml into ClickHouse.
// Off by default - most dev runs don't need an object store. Same wiring as
// docker-compose.cold-storage.yml; the credentials are fixed and shell-safe, like the ClickHouse one.
if (bool.TryParse(builder.Configuration["Flare:ColdStorage"], out var coldStorage) && coldStorage)
{
    const string coldBucket = "flare-cold";
    var coldAccessKey = builder.AddParameter("cold-access-key", "flarecold", secret: true);
    var coldSecretKey = builder.AddParameter("cold-secret-key", "flarecold-secret", secret: true);

    var rustfs = builder.AddContainer("rustfs", "rustfs/rustfs:latest")
        .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
        .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
        .WithEnvironment("RUSTFS_ACCESS_KEY", coldAccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", coldSecretKey)
        .WithVolume("flare-rustfs-data", "/data")
        .WithHttpHealthCheck("/health", 200, "s3");

    // RustFS doesn't create buckets on demand and ClickHouse's S3 disk won't either.
    var rustfsInit = builder.AddContainer("rustfs-init", "minio/mc:latest")
        .WithEntrypoint("/bin/sh")
        .WithArgs(
            "-c",
            $"until mc alias set rustfs \"$RUSTFS_ENDPOINT\" \"$RUSTFS_ACCESS_KEY\" \"$RUSTFS_SECRET_KEY\" >/dev/null 2>&1; do sleep 2; done; mc mb --ignore-existing rustfs/{coldBucket}")
        .WithEnvironment("RUSTFS_ENDPOINT", rustfs.GetEndpoint("s3"))
        .WithEnvironment("RUSTFS_ACCESS_KEY", coldAccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", coldSecretKey)
        .WaitFor(rustfs);

    clickhouse
        .WithBindMount("../../db/clickhouse/config/cold-storage.xml", "/etc/clickhouse-server/config.d/cold-storage.xml", isReadOnly: true)
        .WithEnvironment("FLARE_COLD_ENDPOINT", ReferenceExpression.Create($"{rustfs.GetEndpoint("s3")}/{coldBucket}/"))
        .WithEnvironment("FLARE_COLD_ACCESS_KEY", coldAccessKey)
        .WithEnvironment("FLARE_COLD_SECRET_KEY", coldSecretKey)
        .WaitForCompletion(rustfsInit);
}

// Redis: durable buffer for the batched ClickHouse insert pipeline (Planning.md's
// "Buffering layer" decision, 2026-08-07 - Redis Streams over an in-memory ring buffer,
// specifically so events survive Flare.Ingest restarting mid-buffer). WithPersistence
// (RDB snapshotting) alongside WithDataVolume so buffered-but-unflushed events also
// survive a *Redis* container restart, not just an ingest restart.
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithPersistence(interval: TimeSpan.FromSeconds(30), keysChangedThreshold: 100);

// Auth's identity store (Users/Sessions/IngestApiKeys) - embedded SQLite, not an
// Aspire-modeled resource with its own container (docker-compose.yml's identity-data
// named volume is the containerized equivalent - deliberately not a fourth backing-store
// service either way, see docs/auth.md). ingest and api both need to resolve to the
// literal same file: a repo-relative, gitignored .data/identity/ directory does that for
// local `aspire run`, mirroring how WithDataVolume() persists ClickHouse/Redis across
// container restarts. IdentityDbConnectionFactory creates this directory itself if
// missing - nothing here needs to pre-create it.
var identityDbPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".data", "identity", "flare-identity.db"));

// One-shot ClickHouse migration job (ADR-0284): Flare.Api run as `migrate`, the only resource with the
// `clickhousedb` (default) reference. It exits once the schema is current; ingest, api and alert-worker wait
// for that. No launch profile, so it doesn't claim Flare.Api's port (it serves no HTTP).
var migrate = builder.AddDotnetProject("clickhouse-migrate", "../Flare.Api/Flare.Api.csproj", o => o.ExcludeLaunchProfile = true)
    .WithArgs("migrate")
    .WithReference(logsDb)
    .WaitFor(logsDb);

// Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
// Both ports are fixed and unproxied so external OTLP clients (any logger's OTLP
// exporter) can point at them directly using the conventional OTLP port numbers,
// rather than Aspire's dashboard dev-proxy / dynamically-assigned ports.
var ingest = builder.AddDotnetProject("ingest", "../Flare.Ingest/Flare.Ingest.csproj")
    .WaitForCompletion(migrate)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("ConnectionStrings__clickhousedb-ingest", ClickHouseUser("flare_ingest", chIngestPassword))
    .WithEnvironment("Identity__DbPath", identityDbPath)
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "otlp-http");

// Flare.Api: the Query API (search/filter/time-range/aggregate) over the same
// `clickhousedb.logs` table Flare.Ingest writes to. Unlike Ingest's fixed, unproxied
// OTLP ports (external loggers need those conventional port numbers), this is a normal
// proxied Aspire HTTP endpoint - callers (curl today, the not-yet-built dashboard SPA
// later) go through Aspire's dev-proxy/service discovery like any other resource.
var api = builder.AddDotnetProject("api", "../Flare.Api/Flare.Api.csproj")
    .WaitForCompletion(migrate)
    // Redis: the live-tail streaming endpoint's LogTailBroadcaster reads new entries off
    // the same `flare:logs` stream Flare.Ingest buffers into (plain XREAD, no consumer
    // group - doesn't touch Flare.Ingest's flare-ingest group/PEL accounting).
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("ConnectionStrings__clickhousedb-query", ClickHouseUser("flare_query", chQueryPassword))
    .WithEnvironment("ConnectionStrings__clickhousedb-app", ClickHouseUser("flare_app", chAppPassword))
    .WithEnvironment("Identity__DbPath", identityDbPath)
    // The dashboard isn't an Aspire resource here (it's run separately, e.g. `npm run
    // dev` - see src/dashboard/README.md), so there's no resource reference to pull this
    // from; SvelteKit/Vite's own dev-server default port. Override via user secrets
    // (Cors:AllowedOrigins:0) if your dashboard dev server runs on a different port.
    .WithEnvironment("Cors__AllowedOrigins__0", "http://localhost:5173")
    .WithHttpHealthCheck("/health");

// Flare.AlertWorker: periodic alert-rule evaluation, split out of Flare.Api into its own
// process (docs-internal/adr/0018-alert-worker-extraction.md) so that restarting `api`
// no longer also stops alert evaluation. Same ClickHouse/Redis references as `api` -
// AlertQueryService/CompositeAlertNotifier are reused from Flare.Api via a
// ProjectReference, not a new backing store. No Identity__DbPath/Cors - it never touches
// either. Connects only as flare_app: it runs no migrations, so it gets no `clickhousedb` reference.
var alertWorker = builder.AddDotnetProject("alert-worker", "../Flare.AlertWorker/Flare.AlertWorker.csproj")
    .WithEnvironment("ConnectionStrings__clickhousedb-app", ClickHouseUser("flare_app", chAppPassword))
    .WaitForCompletion(migrate)
    .WithReference(redis)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health");

builder.Build().Run();