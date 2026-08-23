using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// <c>flare instances list</c> - every Flare instance on this machine: the default one
/// at <c>~/.flare/</c> (if initialized) plus every named one under
/// <c>~/.flare/instances/</c> (see <see cref="FlareHome.ListNamedInstances"/>). Discovery
/// is a plain directory scan, not a registry file - no second source of truth to drift
/// out of sync with reality.
/// </summary>
internal sealed class InstancesListCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var instances = new List<FlareInstance>();
        var defaultInstance = FlareHome.Resolve(null);
        if (defaultInstance.IsInitialized)
        {
            instances.Add(defaultInstance);
        }

        instances.AddRange(FlareHome.ListNamedInstances());

        if (instances.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No instances yet - run `flare start` to create the default one, or `flare start -n <NAME>` for a named one.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Directory");
        table.AddColumn("Running");
        table.AddColumn("Image tag");

        foreach (var instance in instances)
        {
            var runningResult = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.Service}}", "--status", "running"], cancellationToken);
            var runningCount = runningResult.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;

            var tag = instance.ReadEnvValue("FLARE_IMAGE_TAG", "(unknown)");
            var runningDisplay = runningCount > 0 ? $"[green]{runningCount} service(s)[/]" : "[grey]stopped[/]";

            table.AddRow(instance.DisplayName, Markup.Escape(instance.Directory), runningDisplay, Markup.Escape(tag));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
