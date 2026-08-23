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
        alerts.AddCommand<AlertsTestCommand>("test")
            .WithDescription("Dry-run fire a saved alert rule (ignores cooldown, sends no notification).");
    });
    config.AddBranch("apikey", apikey =>
    {
        apikey.AddCommand<ApiKeyCreateCommand>("create")
            .WithDescription("Create a new ingest API key.");
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
