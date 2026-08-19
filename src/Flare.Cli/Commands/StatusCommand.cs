using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class StatusCommand : AsyncCommand
{
    private static readonly (string Service, string PortLabel)[] Services =
    [
        ("clickhouse", "localhost:{CLICKHOUSE_HTTP_PORT}"),
        ("redis", "(internal only)"),
        ("ingest", "localhost:{FLARE_INGEST_GRPC_PORT} grpc / {FLARE_INGEST_HTTP_PORT} http"),
        ("api", "localhost:{FLARE_API_PORT}"),
        ("dashboard", "localhost:{FLARE_DASHBOARD_PORT}"),
    ];

    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start`.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Service");
        table.AddColumn("State");
        table.AddColumn("Health");
        table.AddColumn("Port");

        foreach (var (service, portLabelTemplate) in Services)
        {
            var stateResult = await ComposeRunner.RunCapturedAsync(["ps", "--format", "{{.State}}", service]);
            var healthResult = await ComposeRunner.RunCapturedAsync(["ps", "--format", "{{.Health}}", service]);

            var state = stateResult.StandardOutput.Trim();
            var health = healthResult.StandardOutput.Trim();

            var stateDisplay = string.IsNullOrEmpty(state) ? "[red]not running[/]" : Markup.Escape(state);
            var healthDisplay = string.IsNullOrEmpty(health) ? "[grey]n/a[/]" : health switch
            {
                "healthy" => "[green]healthy[/]",
                "starting" => "[yellow]starting[/]",
                _ => $"[red]{Markup.Escape(health)}[/]",
            };

            var portLabel = ResolvePortLabel(portLabelTemplate);

            table.AddRow(service, stateDisplay, healthDisplay, portLabel);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string ResolvePortLabel(string template)
    {
        var result = template;
        foreach (var (key, fallback) in new[]
                 {
                     ("CLICKHOUSE_HTTP_PORT", "8123"),
                     ("FLARE_INGEST_GRPC_PORT", "4317"),
                     ("FLARE_INGEST_HTTP_PORT", "4318"),
                     ("FLARE_API_PORT", "8080"),
                     ("FLARE_DASHBOARD_PORT", "7777"),
                 })
        {
            result = result.Replace($"{{{key}}}", FlareHome.ReadEnvValue(key, fallback));
        }

        return result;
    }
}
