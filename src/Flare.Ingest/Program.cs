using ClickHouse.Driver;
using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
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

// Ingestion-page operational stats (Planning.md v8) - shares the same Redis connection
// as the sinks above rather than adding new infrastructure.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIngestionStatsTracker, RedisIngestionStatsTracker>();

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

app.MapDefaultEndpoints();

app.MapGrpcService<OtlpGrpcLogsService>();
app.MapOtlpHttpLogsEndpoint();

app.MapGrpcService<OtlpGrpcTraceService>();
app.MapOtlpHttpTraceEndpoint();

app.MapGrpcService<OtlpGrpcMetricsService>();
app.MapOtlpHttpMetricsEndpoint();

app.Run();