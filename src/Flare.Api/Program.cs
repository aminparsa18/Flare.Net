using ClickHouse.Driver;
using Flare.Api.Alerting;
using Flare.Api.Endpoints;
using Flare.Api.LiveTail;
using Flare.Api.Query;
using Flare.ServiceDefaults.ClickHouseMigrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Same connection name Flare.AppHost references onto Flare.Ingest - both projects read/
// write the same `clickhousedb.logs` table.
builder.AddClickHouseDataSource(connectionName: "clickhousedb");

// Redis: the live-tail endpoint's LogTailBroadcaster reads new entries off the same
// `flare:logs` stream Flare.Ingest's RedisStreamLogEventSink writes into. Same connection
// name Flare.AppHost references onto Flare.Ingest.
builder.AddRedisClient(connectionName: "redis");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILogQueryService, LogQueryService>();
builder.Services.AddSingleton<ISpanQueryService, SpanQueryService>();
builder.Services.AddSingleton<IMetricQueryService, MetricQueryService>();
builder.Services.AddSingleton<IAlertQueryService, AlertQueryService>();
builder.Services.AddSingleton<ISavedViewQueryService, SavedViewQueryService>();
builder.Services.AddSingleton<IIngestionStatsQueryService, IngestionStatsQueryService>();
builder.Services.Configure<LiveTailOptions>(builder.Configuration.GetSection(LiveTailOptions.SectionName));
builder.Services.AddSingleton<LogTailBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogTailBroadcaster>());

builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// Named/typed HttpClients so the webhook/Slack and Telegram senders inherit
// AddServiceDefaults()'s ConfigureHttpClientDefaults (resilience handler + service
// discovery) for free. Registered as their own concrete types, not IAlertNotifier -
// CompositeAlertNotifier (the one actually registered as IAlertNotifier below) holds all
// three and picks per-rule which one to delegate to. EmailAlertNotifier gets no typed
// HttpClient - MailKit's SmtpClient is its own socket-based client, not HTTP.
builder.Services.AddHttpClient<WebhookAlertNotifier>("alert-webhook");
builder.Services.AddHttpClient<TelegramAlertNotifier>("alert-telegram");
builder.Services.AddSingleton<EmailAlertNotifier>();
builder.Services.AddSingleton<IAlertNotifier, CompositeAlertNotifier>();
builder.Services.AddHostedService<AlertEvaluationWorker>();

builder.Services.AddOpenApi();

// Permissive CORS for every environment, not just dev: v1 has no auth story anywhere
// yet (Planning.md's roadmap lists it as a later item), so this isn't loosening the
// product's actual security posture - it's what lets the still-undecided dashboard SPA
// (Planning.md open question #1) call this API cross-origin once it exists, in exactly
// the self-hosted deployment this product ships as. Revisit once auth lands.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Apply any pending db/clickhouse/*.sql migrations before mapping any endpoints below
// (they assume the schema already exists) - see ClickHouseMigrationRunner's remarks for
// why docker-entrypoint-initdb.d alone isn't enough once a deployment has real data on
// disk. Safe to run unconditionally on every startup: every migration is idempotent,
// and safe to run from both Flare.Api and Flare.Ingest independently (no ordering
// requirement between them).
await ClickHouseMigrationRunner.ApplyAsync(
    app.Services.GetRequiredService<IClickHouseClient>(),
    app.Logger,
    CancellationToken.None);

app.UseCors();
app.UseWebSockets();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapLogsEndpoints();
app.MapLogTailEndpoints();
app.MapSpanEndpoints();
app.MapMetricsEndpoints();
app.MapAlertEndpoints();
app.MapSavedViewEndpoints();
app.MapIngestionEndpoints();

app.Run();
