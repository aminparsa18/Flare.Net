using ClickHouse.Driver;
using Flare.Identity;
using Flare.Ingest.Auth;
using Flare.Ingest.Forwarding;
using Flare.Ingest.Otlp;
using Flare.Ingest.Patterns;
using Flare.Ingest.Pipeline;
using Flare.Ingest.Pipeline.LogMetrics;
using Flare.Ingest.Pipeline.MetricRules;
using Flare.Ingest.Pipeline.Rules;
using Flare.Ingest.Prometheus;
using Flare.Ingest.Sampling;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Flare.ServiceDefaults.ClickHouseMigrations;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Two Kestrel listeners in one process, matching OTLP's conventional ports:
//   4317 - gRPC (cleartext HTTP/2, no ALPN negotiation needed since the protocol is pinned per-listener)
//   4318 - HTTP/1.1, both application/x-protobuf and application/json bodies on POST /v1/logs
//
// Both transports' default size caps (gRPC 4 MB, Kestrel ~30 MB) sit below the 64 MiB an
// OTel SDK exporter sends by default - see OtlpReceiverOptions.MaxRequestSizeBytes.
var otlpReceiverOptions = builder.Configuration.GetSection(OtlpReceiverOptions.SectionName).Get<OtlpReceiverOptions>()
    ?? new OtlpReceiverOptions();
if (otlpReceiverOptions.MaxRequestSizeBytes is <= 0 or > int.MaxValue)
{
    throw new InvalidOperationException(
        $"{OtlpReceiverOptions.SectionName}:{nameof(OtlpReceiverOptions.MaxRequestSizeBytes)} must be between 1 and {int.MaxValue}.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = otlpReceiverOptions.MaxRequestSizeBytes;
    options.ListenAnyIP(4317, o => o.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(4318, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

// The Collector's otlphttp exporter (and most OTLP/HTTP SDKs) gzip request bodies by
// default. The built-in providers handle gzip/deflate/br via Content-Encoding and keep
// enforcing Kestrel's MaxRequestBodySize on the decompressed stream.
builder.Services.AddRequestDecompression();

// Browser OTLP exporters (OTel-JS, Faro) are cross-origin, so they need a CORS preflight
// answer. An origin is allowed if it is in Otlp:AllowedOrigins or listed on any active
// ingest key (ADR-0149); with neither, no CORS headers are sent. The preflight carries no
// key, so which key may use which origin is enforced on the actual request, in
// IngestApiKeyValidationMiddleware.
const string BrowserCorsPolicy = "OtlpBrowser";
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IngestApiKeyCache>((cors, keyCache) =>
    cors.AddPolicy(BrowserCorsPolicy, policy =>
    {
        var origins = otlpReceiverOptions.AllowedOrigins;
        policy.SetIsOriginAllowed(origin =>
            origins.Contains("*") || origins.Contains(origin, StringComparer.OrdinalIgnoreCase) || keyCache.AnyKeyAllowsOrigin(origin));
        policy.WithMethods("POST").WithHeaders("Content-Type", "Content-Encoding", "Authorization").SetPreflightMaxAge(TimeSpan.FromHours(1));
    }));

builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = (int)otlpReceiverOptions.MaxRequestSizeBytes);

builder.Services.Configure<LogEventPipelineOptions>(
    builder.Configuration.GetSection(LogEventPipelineOptions.SectionName));
builder.Services.Configure<SpanEventPipelineOptions>(
    builder.Configuration.GetSection(SpanEventPipelineOptions.SectionName));
builder.Services.Configure<MetricEventPipelineOptions>(
    builder.Configuration.GetSection(MetricEventPipelineOptions.SectionName));
builder.Services.Configure<ProfileEventPipelineOptions>(
    builder.Configuration.GetSection(ProfileEventPipelineOptions.SectionName));

// Redis: durable buffer the pipeline writes to (RedisStreamLogEventSink) and reads from
// (ClickHouseFlushWorker). ClickHouse: batched insert destination. Connection names must
// match the resource names Flare.AppHost references onto this project.
builder.AddRedisClient(connectionName: "redis");
builder.AddClickHouseDataSource(connectionName: "clickhousedb");

builder.Services.AddSingleton<ILogEventSink, RedisStreamLogEventSink>();
builder.Services.AddSingleton<IClickHouseLogEventWriter, ClickHouseLogEventWriter>();
builder.Services.AddHostedService<ClickHouseFlushWorker>();

// Log pattern detection (Drain clustering, Planning.md's "another killer feature" item) -
// singleton so DrainPatternMatcher's cluster store persists across flush batches for the
// life of the process (see its own remarks on why an in-memory store's restart reset is
// accepted, not solved). LogPattern:SharedStore picks the cluster store implementation -
// same "config-gated, off by default" shape as ClickHouse:ClusterMode below - so that
// choice must be known before the singleton is registered, not resolved lazily per call.
builder.Services.Configure<LogPatternOptions>(
    builder.Configuration.GetSection(LogPatternOptions.SectionName));
if (builder.Configuration.GetValue<bool>($"{LogPatternOptions.SectionName}:{nameof(LogPatternOptions.SharedStore)}"))
{
    // Shared across replicas (docker-compose.cluster.yml's ingest-1/ingest-2) - the fix
    // for docs/clustering.md's cross-replica PatternId fragmentation. See its remarks.
    builder.Services.AddSingleton<IPatternClusterStore, RedisPatternClusterStore>();
}
else
{
    // Default: per-process only, correct for a single replica, no Redis traffic added.
    builder.Services.AddSingleton<IPatternClusterStore, InMemoryPatternClusterStore>();
}
builder.Services.AddSingleton<ILogPatternMatcher, DrainPatternMatcher>();
builder.Services.AddSingleton<ILogPatternAnnotator, LogPatternAnnotator>();

// User-defined field extraction/redaction (docs-internal/planning/roadmap.md's
// extraction/redaction item; see docs-internal/adr/0033-pipeline-rules-extraction-redaction.md).
// Runs before Drain clustering above - see ClickHouseFlushWorker.FlushAsync's remarks.
// PipelineRuleCache is both its own hosted service (the poll loop) and the singleton
// IPipelineRuleCache PipelineRuleAnnotator reads from - same "one instance, two roles"
// registration as Flare.Api's LogTailBroadcaster.
builder.Services.Configure<PipelineRuleOptions>(
    builder.Configuration.GetSection(PipelineRuleOptions.SectionName));
builder.Services.AddSingleton<IPipelineRuleStore, ClickHousePipelineRuleStore>();
builder.Services.AddSingleton<PipelineRuleCache>();
builder.Services.AddSingleton<IPipelineRuleCache>(sp => sp.GetRequiredService<PipelineRuleCache>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PipelineRuleCache>());
builder.Services.AddSingleton<IPipelineRuleAnnotator, PipelineRuleAnnotator>();

// Spans - a parallel, deliberately un-unified pipeline alongside the logs one above
// (own Redis stream, own flush worker); see SpanFlushWorker's remarks for why.
// Head + tail sampling (ADR-0122) wraps the Redis sink only when Sampling:Enabled - off, the
// sink is the plain one and none of this runs.
var samplingOptions = builder.Configuration.GetSection(TraceSamplingOptions.SectionName).Get<TraceSamplingOptions>()
    ?? new TraceSamplingOptions();
if (samplingOptions.Enabled)
{
    samplingOptions.Validate();
    builder.Services.Configure<TraceSamplingOptions>(builder.Configuration.GetSection(TraceSamplingOptions.SectionName));
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton(new TraceSampler(samplingOptions));
    builder.Services.AddKeyedSingleton<ISpanEventSink, RedisStreamSpanEventSink>(TraceSamplingWorker.InnerSinkKey);
    builder.Services.AddSingleton<ISpanEventSink>(sp => new SamplingSpanEventSink(
        sp.GetRequiredKeyedService<ISpanEventSink>(TraceSamplingWorker.InnerSinkKey),
        sp.GetRequiredService<TraceSampler>(),
        sp.GetRequiredService<IHttpContextAccessor>(),
        sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddHostedService<TraceSamplingWorker>();
}
else
{
    builder.Services.AddSingleton<ISpanEventSink, RedisStreamSpanEventSink>();
}
builder.Services.AddSingleton<IClickHouseSpanWriter, ClickHouseSpanWriter>();
builder.Services.AddHostedService<SpanFlushWorker>();

// Profiles (ADR-0141, OTLP profiles is Alpha) - another parallel pipeline: own Redis stream,
// own flush worker, own ClickHouse table.
builder.Services.AddSingleton<IProfileSampleSink, RedisStreamProfileSampleSink>();
builder.Services.AddSingleton<IClickHouseProfileWriter, ClickHouseProfileWriter>();
builder.Services.AddHostedService<ProfileFlushWorker>();

// Metrics - unlike spans, one shared Redis stream/flush worker for all three point
// types (Gauge/Sum/Histogram); see MetricFlushWorker's remarks for why.
builder.Services.AddSingleton<IMetricEventSink, RedisStreamMetricEventSink>();
builder.Services.AddSingleton<IClickHouseMetricWriter, ClickHouseMetricWriter>();
// Same "one instance, two roles" cache registration as PipelineRuleCache above.
builder.Services.Configure<MetricAttributeRuleOptions>(
    builder.Configuration.GetSection(MetricAttributeRuleOptions.SectionName));
builder.Services.AddSingleton<IMetricAttributeRuleStore, ClickHouseMetricAttributeRuleStore>();
builder.Services.AddSingleton<MetricAttributeRuleCache>();
builder.Services.AddSingleton<IMetricAttributeRuleCache>(sp => sp.GetRequiredService<MetricAttributeRuleCache>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricAttributeRuleCache>());
builder.Services.AddHostedService<MetricFlushWorker>();

// Log-based metrics (ADR-0140): ClickHouseFlushWorker counts matching logs per flush and
// writes them through IClickHouseMetricWriter. Same "one instance, two roles" cache.
builder.Services.Configure<LogMetricOptions>(
    builder.Configuration.GetSection(LogMetricOptions.SectionName));
builder.Services.AddSingleton<ILogMetricStore, ClickHouseLogMetricStore>();
builder.Services.AddSingleton<LogMetricCache>();
builder.Services.AddSingleton<ILogMetricCache>(sp => sp.GetRequiredService<LogMetricCache>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogMetricCache>());
builder.Services.AddSingleton<ILogMetricEmitter, LogMetricEmitter>();

// Native Prometheus scrape (Planning.md v20) - a second, pull-side receiver feeding the
// same IMetricEventSink/pipeline as the OTLP metrics endpoints above. No-ops with zero
// configured targets, see PrometheusScrapeOptions's remarks.
builder.Services.Configure<PrometheusScrapeOptions>(
    builder.Configuration.GetSection(PrometheusScrapeOptions.SectionName));
builder.Services.AddHttpClient("PrometheusScrape");
builder.Services.AddHostedService<PrometheusScrapeWorker>();

// OTLP forwarding (ADR-0155): copies accepted logs/traces/metrics to other OTLP/HTTP endpoints.
// Off with no Forwarding:Targets configured. One instance serves as both the IOtlpForwarder the
// receivers call and the hosted service that drains the per-target queues.
builder.Services.Configure<ForwardingOptions>(builder.Configuration.GetSection(ForwardingOptions.SectionName));
builder.Services.AddHttpClient("OtlpForwarding");
builder.Services.AddSingleton<IForwardingTargetStore, ClickHouseForwardingTargetStore>();
builder.Services.AddSingleton<OtlpForwarder>();
builder.Services.AddSingleton<IOtlpForwarder>(sp => sp.GetRequiredService<OtlpForwarder>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<OtlpForwarder>());

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

// Per-ingest-key usage counters + limit enforcement (ADR-0051) - Redis, not in-process,
// because ingest can run as several replicas and Flare.Api reads the same counters.
builder.Services.AddSingleton<IIngestKeyUsageStore, RedisIngestKeyUsageStore>();

var app = builder.Build();

// Apply any pending db/clickhouse/*.sql migrations before starting the flush workers
// below (they assume the schema already exists) - see ClickHouseMigrationRunner's
// remarks for why docker-entrypoint-initdb.d alone isn't enough once a deployment has
// real data on disk. Safe to run unconditionally on every startup: every migration is
// idempotent, and safe to run from both Flare.Ingest and Flare.Api independently (no
// ordering requirement between them).
//
// ClickHouse:ClusterMode (Planning.md's "Multi-node scaling" item, docs/clustering.md)
// switches to the db/clickhouse-cluster/*.sql schema set instead - set by
// docker-compose.cluster.yml, unset/false everywhere else.
await ClickHouseMigrationRunner.ApplyAsync(
    app.Services.GetRequiredService<IClickHouseClient>(),
    app.Logger,
    CancellationToken.None,
    clusterMode: builder.Configuration.GetValue<bool>("ClickHouse:ClusterMode"));

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

app.UseRequestDecompression();

// Before the key check: a preflight OPTIONS carries no Authorization header, so it must be
// answered here rather than rejected with 401. With no origins configured the policy
// matches nothing, so no CORS headers are emitted.
app.UseCors(BrowserCorsPolicy);

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

app.MapGrpcService<OtlpGrpcProfilesService>();
app.MapOtlpHttpProfilesEndpoint();

app.Run();