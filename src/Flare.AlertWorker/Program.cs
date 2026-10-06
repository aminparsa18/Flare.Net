using Flare.Api.Ai;
using Flare.Api.Alerting;
using Flare.Api.Query;
using Microsoft.AspNetCore.DataProtection;
using Flare.AlertWorker.Alerting;
using Flare.AlertWorker.Synthetic;
using Flare.Api.Synthetic;

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
// Same Query section Flare.Api binds - AlertQueryService reads its execution caps
// (incl. AlertEvaluationMaxExecutionSeconds) from it.
builder.Services.Configure<QueryLimitsOptions>(builder.Configuration.GetSection(QueryLimitsOptions.SectionName));
// Promoted attribute columns (ADR-0062) - log-count conditions are LogFilters, so they
// read the same promoted columns Flare.Api's queries do. Refreshed from system.columns.
builder.Services.AddSingleton<IPromotedAttributeRegistry, PromotedAttributeRegistry>();
builder.Services.AddHostedService<PromotedAttributeRefreshWorker>();
builder.Services.AddSingleton<IAlertQueryService, AlertQueryService>();
builder.Services.AddSingleton<INotificationChannelQueryService, NotificationChannelQueryService>();
builder.Services.AddSingleton<IMaintenanceWindowQueryService, MaintenanceWindowQueryService>();
builder.Services.AddSingleton<IOnCallRotationQueryService, OnCallRotationQueryService>();
builder.Services.AddSingleton<ISloQueryService, SloQueryService>();
builder.Services.AddSingleton<IErrorIssueQueryService, ErrorIssueQueryService>();

builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// The dashboard's public base URL, for the deep link every notifier appends to a real
// fired alert - see AlertLinkOptions. Same "Alerting" section AlertingOptions itself binds
// from, via its own options class in Flare.Api since Flare.Api's own send-test endpoints
// need it too and PollInterval/MaxRulesPerTick are meaningless there.
builder.Services.Configure<AlertLinkOptions>(builder.Configuration.GetSection(AlertLinkOptions.SectionName));
// Signs the acknowledge link ({{ack_url}}, ADR-0127). Flare.Api redeems it, so both processes must share one
// Data Protection key ring: same application name and Redis key as Flare.Api's Program.cs.
builder.Services.AddDataProtection()
    .SetApplicationName("Flare")
    .PersistKeysToStackExchangeRedis(
        () => StackExchange.Redis.ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("redis")!).GetDatabase(),
        "Flare-DataProtection-Keys");
builder.Services.AddSingleton<IAlertAckLinkSigner, AlertAckLinkSigner>();
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
builder.Services.AddHttpClient<TeamsAlertNotifier>("alert-teams");
builder.Services.AddHttpClient<JiraAlertNotifier>("alert-jira");
builder.Services.AddHttpClient<IncidentIoAlertNotifier>("alert-incidentio");
builder.Services.AddHttpClient<JsmOpsAlertNotifier>("alert-jsmops");
builder.Services.AddHttpClient<DiscordAlertNotifier>("alert-discord");
builder.Services.AddSingleton<EmailAlertNotifier>();
// Registered as its own concrete type (not just IAlertNotifier) so AlertEvaluationWorker
// can inject it directly for SendAllAsync, the fan-out entrypoint that isn't part of the
// IAlertNotifier interface - same registration shape as Flare.Api's own Program.cs.
builder.Services.AddSingleton<CompositeAlertNotifier>();
builder.Services.AddSingleton<IAlertNotifier>(sp => sp.GetRequiredService<CompositeAlertNotifier>());

// AI incident summaries (ADR-0104): opt-in via Ai__Enabled + Ai__IncidentSummaries, same Ai section
// Flare.Api binds. Its HttpClient drops the resilience handler (10s attempt timeout, retries) - the
// LLM client owns the timeout and a model call must not be re-sent.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
builder.Services.AddHttpClient(OpenAiCompatibleLlmClient.HttpClientName)
    .RemoveAllResilienceHandlers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
#pragma warning restore EXTEXP0001
builder.Services.AddSingleton<ILlmClient, OpenAiCompatibleLlmClient>();
builder.Services.AddSingleton<IAlertEvidenceQueryService, AlertEvidenceQueryService>();
builder.Services.AddSingleton<IIncidentSummaryService, IncidentSummaryService>();

builder.Services.AddHostedService<AlertEvaluationWorker>();

// Synthetic monitoring (ADR-0128). The probe client drops the resilience handler (retries, 10s attempt
// timeout): a probe is one honest attempt bounded by the monitor's own timeout.
builder.Services.Configure<SyntheticOptions>(builder.Configuration.GetSection(SyntheticOptions.SectionName));
builder.Services.AddSingleton<ISyntheticMonitorQueryService, SyntheticMonitorQueryService>();
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
builder.Services.AddHttpClient(SyntheticProber.HttpClientName).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddSingleton<SyntheticProber>();
builder.Services.AddSingleton<SyntheticResultWriter>();
builder.Services.AddHostedService<SyntheticProbeWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
