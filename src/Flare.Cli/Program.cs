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
    config.AddBranch("config", configBranch =>
    {
        configBranch.AddCommand<ConfigExportCommand>("export")
            .WithDescription("Export alerts, channels, SLOs, pipeline rules, maintenance windows, metric attribute rules and ingest-key limits as one portable JSON file (by-name references, credentials as ${ENV_VAR}).");
        configBranch.AddCommand<ConfigApplyCommand>("apply")
            .WithDescription("Create or update everything in a `flare config export` file (matched by name; --dry-run to preview; nothing is deleted).");
    });
    config.AddBranch("apikey", apikey =>
    {
        apikey.AddCommand<ApiKeyCreateCommand>("create")
            .WithDescription("Create a new ingest API key (optionally restricted to --origin/--service).");
        apikey.AddCommand<ApiKeyScopeCommand>("scope")
            .WithDescription("Restrict an ingest key to browser origins and/or services.");
    });
    config.AddBranch("sourcemaps", sourcemaps =>
    {
        sourcemaps.AddCommand<SourceMapsUploadCommand>("upload")
            .WithDescription("Upload JavaScript source maps so browser stack traces on /errors show original sources.");
        sourcemaps.AddCommand<SourceMapsListCommand>("list")
            .WithDescription("List uploaded source maps.");
        sourcemaps.AddCommand<SourceMapsDeleteCommand>("delete")
            .WithDescription("Delete a release's source maps (or one bundle's).");
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
    config.AddBranch("retention", retention =>
    {
        retention.AddCommand<RetentionShowCommand>("show")
            .WithDescription("Show data retention per signal, as ClickHouse has it now.");
        retention.AddCommand<RetentionSetCommand>("set")
            .WithDescription("Set a signal's retention, cold-storage tiering and per-resource rules (admin).");
    });
    config.AddBranch("forwarding", forwarding =>
    {
        forwarding.AddCommand<ForwardingListCommand>("list")
            .WithDescription("List managed OTLP forwarding targets (admin).");
        forwarding.AddCommand<ForwardingCreateCommand>("create")
            .WithDescription("Create a forwarding target that copies incoming telemetry to another OTLP endpoint.");
        forwarding.AddCommand<ForwardingUpdateCommand>("update")
            .WithDescription("Update a forwarding target (only the options you pass change).");
        forwarding.AddCommand<ForwardingDeleteCommand>("delete")
            .WithDescription("Delete a forwarding target and its queue.");
        forwarding.AddCommand<ForwardingStatusCommand>("status")
            .WithDescription("Show queue depth and delivery counters per forwarding target.");
    });
    config.AddBranch("archive", archive =>
    {
        archive.AddCommand<ArchiveShowCommand>("show")
            .WithDescription("Show the saved S3 archive settings (keys masked).");
        archive.AddCommand<ArchiveSetCommand>("set")
            .WithDescription("Save S3 archive settings (only the options you pass change after the first save).");
        archive.AddCommand<ArchiveResetCommand>("reset")
            .WithDescription("Delete the saved settings so the archive follows configuration again.");
        archive.AddCommand<ArchiveStatusCommand>("status")
            .WithDescription("Show the last exported hour and any error per archived table.");
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
