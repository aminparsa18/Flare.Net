using System.Reflection;
using Flare.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("flare");

    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown";
    config.SetApplicationVersion(version);

    config.AddCommand<StartCommand>("start")
        .WithDescription("Start the standing Flare stack (first run also initializes ~/.flare/).");
    config.AddCommand<StopCommand>("stop")
        .WithDescription("Stop the stack without removing data volumes - a pause, not a teardown.");
    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show the stack's current health/state.");
    config.AddCommand<IngestionCommand>("ingestion")
        .WithDescription("Show OTLP ingestion health: verdict, rates, receivers, pipeline buffers/flush workers.");
    config.AddCommand<OpenCommand>("open")
        .WithDescription("Open the dashboard in your default browser.");
    config.AddCommand<McpCommand>("mcp")
        .WithDescription("Run a Model Context Protocol server over stdio so AI assistants (Claude Code, Cursor, VS Code) can query Flare.");
    config.AddCommand<TailCommand>("tail")
        .WithDescription("Live-tail structured log events (filterable by service/level/trace/search).");
    config.AddCommand<SearchCommand>("search")
        .WithDescription("One-shot log search (filterable by service/level/trace/search/since).");
    config.AddCommand<ExportCommand>("export")
        .WithDescription("Export a time range of log events to NDJSON or CSV.");
    config.AddCommand<TracesCommand>("traces")
        .WithDescription("Search recent traces (filterable by service/status/kind/duration/trace-id).");
    config.AddCommand<TraceCommand>("trace")
        .WithDescription("Render one trace as a text waterfall.");
    config.AddCommand<MetricsCommand>("metrics")
        .WithDescription("List discoverable metrics (filterable by service/since).");
    config.AddCommand<MetricCommand>("metric")
        .WithDescription("Chart one metric as ASCII sparklines.");
    // Branches: no top-level `.WithDescription` (IBranchConfigurator doesn't expose one) -
    // each leaf command's own description carries the documentation instead.
    config.AddBranch("alerts", alerts =>
    {
        alerts.AddCommand<AlertsListCommand>("list")
            .WithDescription("List saved alert rules.");
        alerts.AddCommand<AlertsHistoryCommand>("history")
            .WithDescription("Show a rule's recent fired/resolved events, with the AI incident summary when there is one.");
        alerts.AddCommand<AlertsTestCommand>("test")
            .WithDescription("Dry-run fire a saved alert rule (ignores cooldown, sends no notification).");
        alerts.AddCommand<AlertsSendTestCommand>("send-test")
            .WithDescription("Send a real test notification through a saved alert rule's configured channel.");
        alerts.AddCommand<AlertsExportCommand>("export")
            .WithDescription("Export every alert rule as portable JSON (channels/SLOs by name, no ids or credentials).");
        alerts.AddCommand<AlertsImportCommand>("import")
            .WithDescription("Import rules from a `flare alerts export` file (--dry-run to preview; existing names are skipped).");
        alerts.AddCommand<AlertsAckCommand>("ack")
            .WithDescription("Acknowledge a firing rule's incident (stops re-notifications and escalation).");
        alerts.AddCommand<AlertsSnoozeCommand>("snooze")
            .WithDescription("Snooze a firing rule's re-notifications for N minutes (does not stop escalation).");
        alerts.AddCommand<AlertsUnackCommand>("unack")
            .WithDescription("Clear a rule's acknowledgement or snooze.");
    });
    config.AddBranch("apikey", apikey =>
    {
        apikey.AddCommand<ApiKeyCreateCommand>("create")
            .WithDescription("Create a new ingest API key.");
    });
    config.AddBranch("notification-channels", notificationChannels =>
    {
        notificationChannels.AddCommand<NotificationChannelsListCommand>("list")
            .WithDescription("List saved notification channels.");
        notificationChannels.AddCommand<NotificationChannelsCreateCommand>("create")
            .WithDescription("Create a new notification channel.");
        notificationChannels.AddCommand<NotificationChannelsUpdateCommand>("update")
            .WithDescription("Update an existing notification channel.");
        notificationChannels.AddCommand<NotificationChannelsDeleteCommand>("delete")
            .WithDescription("Delete a notification channel.");
        notificationChannels.AddCommand<NotificationChannelsSendTestCommand>("send-test")
            .WithDescription("Send a real test notification through a saved channel.");
    });
    config.AddBranch("synthetic-monitors", syntheticMonitors =>
    {
        syntheticMonitors.AddCommand<SyntheticMonitorsListCommand>("list")
            .WithDescription("List synthetic monitors with their latest result.");
        syntheticMonitors.AddCommand<SyntheticMonitorsCreateCommand>("create")
            .WithDescription("Create an HTTP, TCP or TLS probe.");
        syntheticMonitors.AddCommand<SyntheticMonitorsUpdateCommand>("update")
            .WithDescription("Update a monitor (only the options you pass change).");
        syntheticMonitors.AddCommand<SyntheticMonitorsDeleteCommand>("delete")
            .WithDescription("Delete a monitor.");
    });
    config.AddBranch("instances", instances =>
    {
        instances.AddCommand<InstancesListCommand>("list")
            .WithDescription("List every Flare instance on this machine (default plus any named ones).");
    });
    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Pull the latest images for the pinned tag and recreate containers. --tag <TAG> moves the pin itself first.");
    config.AddCommand<LogsCommand>("logs")
        .WithDescription("Show or follow container logs.");
    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Run read-only diagnostics against Docker and the stack.");
    config.AddCommand<DestroyCommand>("destroy")
        .WithDescription("Remove containers AND data volumes. Destructive - requires --yes.");
});

return await app.RunAsync(args);
