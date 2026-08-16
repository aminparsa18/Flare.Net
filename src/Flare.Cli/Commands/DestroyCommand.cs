using System.ComponentModel;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Destructive. Removes containers AND named data volumes (ClickHouse/Redis/identity -
/// all log data and every login/account gone). Refuses to run without explicit
/// confirmation: either `--yes` or an interactive prompt - never proceeds silently on a
/// non-interactive invocation with no flag, so a CI pipeline or a piped script can never
/// wipe data by accident.
/// </summary>
internal sealed class DestroyCommand : AsyncCommand<DestroyCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Skip the interactive confirmation prompt. Required for non-interactive use.")]
        public bool Yes { get; init; }

        [CommandOption("--purge-config")]
        [Description("Also delete ~/.flare/ entirely (including .env credentials), not just the data volumes. A follow-up `flare start` regenerates fresh credentials.")]
        public bool PurgeConfig { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - nothing to destroy.[/]");
            return 0;
        }

        if (!settings.Yes)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine("[red]Refusing to destroy without --yes on a non-interactive invocation.[/]");
                return 1;
            }

            var confirmed = AnsiConsole.Confirm(
                "[red]This deletes all Flare log data, alert history, and every login/account.[/] Continue?",
                defaultValue: false);
            if (!confirmed)
            {
                AnsiConsole.MarkupLine("[grey]Aborted - nothing was removed.[/]");
                return 1;
            }
        }

        var exitCode = await ComposeRunner.RunStreamedAsync(["down", "-v"]);
        if (exitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose down -v` failed - see the output above.");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓[/] Removed containers and data volumes (clickhouse-data, redis-data, identity-data).");

        if (settings.PurgeConfig)
        {
            Directory.Delete(FlareHome.Directory, recursive: true);
            AnsiConsole.MarkupLine($"[green]✓[/] Removed {FlareHome.Directory} entirely - the next `flare start` regenerates fresh credentials.");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Kept ~/.flare/.env - `flare start` will reuse the same credentials against fresh, empty volumes.[/]");
        }

        return 0;
    }
}
