using ClickHouse.Driver;
using Flare.Identity;
using Flare.Ingest.Auth;
using Flare.Ingest.Otlp;
using Flare.Ingest.Patterns;
using Flare.Ingest.Pipeline;
using Flare.Ingest.Prometheus;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Flare.ServiceDefaults.ClickHouseMigrations;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Two Kestrel listeners in one process, matching OTLP's conventional ports:
//   4317 - gRPC (cleartext HTTP/2, no ALPN negotiation needed since the protocol is pinned per-listener)
//   4318 - HTTP/1.1, both application/x-protobuf and application/json bodies on POST /v1/logs
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(4317, o => o.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(4318, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddGrpc();

builder.Services.Configure<LogEventPipelineOptions>(
    builder.Configuration.GetSection(LogEventPipelineOptions.SectionName));
builder.Services.Configure<SpanEventPipelineOptions>(
    builder.Configuration.GetSection(SpanEventPipelineOptions.SectionName));
builder.Services.Configure<MetricEventPipelineOptions>(
    builder.Configuration.GetSection(MetricEventPipelineOptions.SectionName));

// Redis: durable buffer the pipeline writes to (RedisStreamLogEventSink) and reads from
// (ClickHouseFlushWorker). ClickHouse: batched insert destination. Connection names must
// match the resource names Flare.AppHost references onto this project.
builder.AddRedisClient(connectionName: "redis");
builder.AddClickHouseDataSource(connectionName: "clickhousedb");

builder.Services.AddSingleton<ILogEventSink, RedisStreamLogEventSink>();
builder.Services.AddSingleton<IClickHouseLogEventWriter, ClickHouseLogEventWriter>();
builder.Services.AddHostedService<ClickHouseFlushWorker>();

// Log pattern detection (Drain clustering, Planning.md's "another killer feature" item) -
// singleton so DrainPatternMatcher's in-memory tree persists across flush batches for the
// life of the process (see its own remarks on why that's an accepted, not a solved,
// restart-reset limitation).
builder.Services.Configure<LogPatternOptions>(
    builder.Configuration.GetSection(LogPatternOptions.SectionName));
builder.Services.AddSingleton<ILogPatternMatcher, DrainPatternMatcher>();
builder.Services.AddSingleton<ILogPatternAnnotator, LogPatternAnnotator>();

// Spans - a parallel, deliberately un-unified pipeline alongside the logs one above
// (own Redis stream, own flush worker); see SpanFlushWorker's remarks for why.
builder.Services.AddSingleton<ISpanEventSink, RedisStreamSpanEventSink>();
builder.Services.AddSingleton<IClickHouseSpanWriter, ClickHouseSpanWriter>();
builder.Services.AddHostedService<SpanFlushWorker>();

// Metrics - unlike spans, one shared Redis stream/flush worker for all three point
// types (Gauge/Sum/Histogram); see MetricFlushWorker's remarks for why.
builder.Services.AddSingleton<IMetricEventSink, RedisStreamMetricEventSink>();
builder.Services.AddSingleton<IClickHouseMetricWriter, ClickHouseMetricWriter>();
builder.Services.AddHostedService<MetricFlushWorker>();

// Native Prometheus scrape (Planning.md v20) - a second, pull-side receiver feeding the
// same IMetricEventSink/pipeline as the OTLP metrics endpoints above. No-ops with zero
// configured targets, see PrometheusScrapeOptions's remarks.
builder.Services.Configure<PrometheusScrapeOptions>(
    builder.Configuration.GetSection(PrometheusScrapeOptions.SectionName));
builder.Services.AddHttpClient("PrometheusScrape");
builder.Services.AddHostedService<PrometheusScrapeWorker>();

// Ingestion-page operational stats (Planning.md v8) - shares the same Redis connection
// as the sinks above rather than adding new infrastructure.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIngestionStatsTracker, RedisIngestionStatsTracker>();

// Pipeline-health tracking (Planning.md v10) - each *FlushWorker* above records its own
// last-flush outcome here; stream/consumer-group depth itself is read live from Redis by
// Flare.Api, not tracked separately.
builder.Services.AddSingleton<IFlushHealthTracker, RedisFlushHealthTracker>();

// Ingest API key validation (Planning.md's "Auth + multi-user / roles" item, ingest-side
// half) - narrow wiring only (IIngestApiKeyStore against the shared identity SQLite
// file), no Users/Sessions here, see Flare.Identity's remarks for why.
builder.AddFlareIngestAuth();
builder.Services.Configure<IngestAuthOptions>(builder.Configuration.GetSection(IngestAuthOptions.SectionName));
builder.Services.AddSingleton<IngestApiKeyCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestApiKeyCache>());

var app = builder.Build();

// Apply any pending db/clickhouse/*.sql migrations before starting the flush workers
// below (they assume the schema already exists) - see ClickHouseMigrationRunner's
// remarks for why docker-entrypoint-initdb.d alone isn't enough once a deployment has
// real data on disk. Safe to run unconditionally on every startup: every migration is
// idempotent, and safe to run from both Flare.Ingest and Flare.Api independently (no
// ordering requirement between them).
await ClickHouseMigrationRunner.ApplyAsync(
    app.Services.GetRequiredService<IClickHouseClient>(),
    app.Logger,
    CancellationToken.None);

// Same idempotent-migration convention, applied from both Flare.Ingest and Flare.Api
// independently (see IdentityMigrationRunner's remarks) - Ingest needs the IngestApiKeys
// table to exist regardless of which process happens to start first.
await IdentityMigrationRunner.ApplyAsync(
    app.Services.GetRequiredService<IdentityDbConnectionFactory>(),
    app.Logger,
    CancellationToken.None);

// Populate the cache before accepting any traffic - IngestApiKeyCache's own background
// refresh loop wouldn't run its first tick for RefreshInterval (30s) otherwise, during
// which every request would be rejected as if zero keys existed.
await app.Services.GetRequiredService<IngestApiKeyCache>().InitializeAsync(CancellationToken.None);

app.MapDefaultEndpoints();

// Must come after MapDefaultEndpoints() (so /health and /alive stay reachable
// unconditionally - see the middleware's own remarks for why this is a positive
// allow-list, not a /health exclusion) and before every OTLP Map* call below.
app.UseMiddleware<IngestApiKeyValidationMiddleware>();

app.MapGrpcService<OtlpGrpcLogsService>();
app.MapOtlpHttpLogsEndpoint();

app.MapGrpcService<OtlpGrpcTraceService>();
app.MapOtlpHttpTraceEndpoint();

app.MapGrpcService<OtlpGrpcMetricsService>();
app.MapOtlpHttpMetricsEndpoint();

app.Run();