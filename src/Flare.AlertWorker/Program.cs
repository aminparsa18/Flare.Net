using Flare.Api.Alerting;
using Flare.Api.Query;
using Flare.AlertWorker.Alerting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Same connection names Flare.AppHost/docker-compose.yml reference onto Flare.Ingest/
// Flare.Api - this process reads the alert_rules/alert_events/logs tables ClickHouse
// migrations (applied by Flare.Ingest/Flare.Api at their own startup) already created,
// and shares the same per-tick evaluation lock (see AlertEvaluationWorker's remarks)
// through the same Redis every other process already depends on. No new backing store.
builder.AddClickHouseDataSource(connectionName: "clickhousedb");
builder.AddRedisClient(connectionName: "redis");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAlertQueryService, AlertQueryService>();

builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// The dashboard's public base URL, for the deep link every notifier appends to a real
// fired alert - see AlertLinkOptions. Same "Alerting" section AlertingOptions itself binds
// from, via its own options class in Flare.Api since Flare.Api's own send-test endpoints
// need it too and PollInterval/MaxRulesPerTick are meaningless there.
builder.Services.Configure<AlertLinkOptions>(builder.Configuration.GetSection(AlertLinkOptions.SectionName));
// Named/typed HttpClients so the webhook/Slack, Telegram, and PagerDuty senders inherit
// AddServiceDefaults()'s ConfigureHttpClientDefaults (resilience handler + service
// discovery) for free - same registration Flare.Api's own Program.cs makes for its
// send-test endpoints. Registered as their own concrete types, not IAlertNotifier -
// CompositeAlertNotifier (the one actually registered as IAlertNotifier below) holds all
// four and picks per-rule which one to delegate to. EmailAlertNotifier gets no typed
// HttpClient - MailKit's SmtpClient is its own socket-based client, not HTTP.
builder.Services.AddHttpClient<WebhookAlertNotifier>("alert-webhook");
builder.Services.AddHttpClient<TelegramAlertNotifier>("alert-telegram");
builder.Services.AddHttpClient<PagerDutyAlertNotifier>("alert-pagerduty");
builder.Services.AddSingleton<EmailAlertNotifier>();
builder.Services.AddSingleton<IAlertNotifier, CompositeAlertNotifier>();

builder.Services.AddHostedService<AlertEvaluationWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
