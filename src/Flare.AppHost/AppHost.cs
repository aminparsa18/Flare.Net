var builder = DistributedApplication.CreateBuilder(args);

// ClickHouse: log storage. The connection string `ingest` gets below via WithReference
// is consumed by Flare.Ingest's ClickHouseFlushWorker (see db/clickhouse/README.md).
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

// Redis: durable buffer for the batched ClickHouse insert pipeline (Planning.md's
// "Buffering layer" decision, 2026-08-07 - Redis Streams over an in-memory ring buffer,
// specifically so events survive Flare.Ingest restarting mid-buffer). WithPersistence
// (RDB snapshotting) alongside WithDataVolume so buffered-but-unflushed events also
// survive a *Redis* container restart, not just an ingest restart.
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithPersistence(interval: TimeSpan.FromSeconds(30), keysChangedThreshold: 100);

// Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
// Both ports are fixed and unproxied so external OTLP clients (any logger's OTLP
// exporter) can point at them directly using the conventional OTLP port numbers,
// rather than Aspire's dashboard dev-proxy / dynamically-assigned ports.
var ingest = builder.AddProject<Projects.Flare_Ingest>("ingest")
    .WithReference(logsDb)
    .WaitFor(logsDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "otlp-http");

// Flare.Api: the Query API (search/filter/time-range/aggregate) over the same
// `clickhousedb.logs` table Flare.Ingest writes to. Unlike Ingest's fixed, unproxied
// OTLP ports (external loggers need those conventional port numbers), this is a normal
// proxied Aspire HTTP endpoint - callers (curl today, the not-yet-built dashboard SPA
// later) go through Aspire's dev-proxy/service discovery like any other resource.
var api = builder.AddProject<Projects.Flare_Api>("api")
    .WithReference(logsDb)
    .WaitFor(logsDb)
    // Redis: the live-tail streaming endpoint's LogTailBroadcaster reads new entries off
    // the same `flare:logs` stream Flare.Ingest buffers into (plain XREAD, no consumer
    // group - doesn't touch Flare.Ingest's flare-ingest group/PEL accounting).
    .WithReference(redis)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health");

builder.Build().Run();