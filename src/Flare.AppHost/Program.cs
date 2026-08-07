var builder = DistributedApplication.CreateBuilder(args);

// ClickHouse: log storage. Schema scaffolding only, for the "Internal log-event model +
// ClickHouse schema" roadmap item - nothing in Flare.Ingest writes to it yet (see
// db/clickhouse/README.md). The batched insert pipeline (a later roadmap item) is what
// actually consumes the connection string `ingest` gets below via WithReference.
//
// The `Aspire.Hosting.ClickHouse` resource has no ClickHouse-specific init-script hook
// (no WithInitFiles/WithInitBindMount, unlike e.g. the Postgres integration - confirmed
// against its docs/source, not assumed). Instead this uses the generic
// ContainerResourceBuilderExtensions.WithBindMount(source, target, isReadOnly) - any
// Aspire container resource - to mount db/clickhouse/ at
// /docker-entrypoint-initdb.d, the ClickHouse *official* Docker image's own
// (Aspire-independent) convention for running *.sql files once, in filename order, the
// first time the container starts against an empty data directory.
var clickhouse = builder.AddClickHouse("clickhouse")
    .WithDataVolume()
    .WithBindMount("../../db/clickhouse", "/docker-entrypoint-initdb.d", isReadOnly: true);
var logsDb = clickhouse.AddDatabase("clickhousedb");

// Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
// Both ports are fixed and unproxied so external OTLP clients (any logger's OTLP
// exporter) can point at them directly using the conventional OTLP port numbers,
// rather than Aspire's dashboard dev-proxy / dynamically-assigned ports.
var ingest = builder.AddProject<Projects.Flare_Ingest>("ingest")
    .WithReference(logsDb)
    .WaitFor(logsDb)
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "otlp-http");

builder.Build().Run();