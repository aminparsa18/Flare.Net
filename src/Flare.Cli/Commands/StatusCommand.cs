using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    private static readonly (string Service, string PortLabel)[] Services =
    [
        ("clickhouse", "localhost:{CLICKHOUSE_HTTP_PORT}"),
        ("redis", "(internal only)"),
        ("ingest", "localhost:{FLARE_INGEST_GRPC_PORT} grpc / {FLARE_INGEST_HTTP_PORT} http"),
        ("api", "localhost:{FLARE_API_PORT}"),
        ("dashboard", "localhost:{FLARE_DASHBOARD_PORT}"),
    ];

    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - run `{instance.StartHint}`.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Service");
        table.AddColumn("State");
        table.AddColumn("Health");
        table.AddColumn("Port");

        foreach (var (service, portLabelTemplate) in Services)
        {
            var stateResult = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.State}}", service]);
            var healthResult = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.Health}}", service]);

            var state = stateResult.StandardOutput.Trim();
            var health = healthResult.StandardOutput.Trim();

            var stateDisplay = string.IsNullOrEmpty(state) ? "[red]not running[/]" : Markup.Escape(state);
            var healthDisplay = string.IsNullOrEmpty(health) ? "[grey]n/a[/]" : health switch
            {
                "healthy" => "[green]healthy[/]",
                "starting" => "[yellow]starting[/]",
                _ => $"[red]{Markup.Escape(health)}[/]",
            };

            var portLabel = ResolvePortLabel(instance, portLabelTemplate);

            table.AddRow(service, stateDisplay, healthDisplay, portLabel);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string ResolvePortLabel(FlareInstance instance, string template)
    {
        var result = template;
        foreach (var (_, envKey, fallback) in PortDefaults.All)
        {
            result = result.Replace($"{{{envKey}}}", instance.ReadEnvValue(envKey, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return result;
    }
}
