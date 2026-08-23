using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
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

        var profile = FlareHome.ResolveTopology(instance);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Service");
        table.AddColumn("State");
        table.AddColumn("Health");
        table.AddColumn("Port");

        foreach (var (service, portLabelTemplate) in profile.StatusRows)
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

            var portLabel = ResolvePortLabel(instance, profile, portLabelTemplate);

            table.AddRow(service, stateDisplay, healthDisplay, portLabel);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string ResolvePortLabel(FlareInstance instance, TopologyProfile profile, string template)
    {
        var result = template;
        foreach (var (_, envKey, fallback) in profile.Ports)
        {
            result = result.Replace($"{{{envKey}}}", instance.ReadEnvValue(envKey, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return result;
    }
}
