using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class StopCommand : AsyncCommand<StopCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.Resolve(settings.InstanceName);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Not initialized yet - nothing to stop. Run `{instance.StartHint}` first.[/]");
            return 0;
        }

        // `stop`, deliberately NOT `down` - containers stop, volumes/networks stay, so
        // clickhouse-data/redis-data/identity-data all persist. This is a pause, not a
        // teardown; `flare destroy` is the explicit, confirmed-only teardown path.
        var exitCode = await ComposeRunner.RunStreamedAsync(instance, ["stop"]);
        if (exitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose stop` failed - see the output above.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Stopped. Data volumes were kept - `{instance.StartHint}` picks up where you left off.");
        return 0;
    }
}
