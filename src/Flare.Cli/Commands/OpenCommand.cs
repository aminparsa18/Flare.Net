using System.Diagnostics;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class OpenCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        var dashboardState = await ComposeRunner.RunCapturedAsync(["ps", "--format", "{{.State}}", "dashboard"]);
        if (!string.Equals(dashboardState.StandardOutput.Trim(), "running", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]✗[/] The dashboard isn't running - run `flare start` (or check `flare status`) first.");
            return 1;
        }

        var port = FlareHome.ReadEnvValue("FLARE_DASHBOARD_PORT", "3000");
        var url = $"http://localhost:{port}";

        if (!TryOpenBrowser(url))
        {
            AnsiConsole.MarkupLine($"[yellow]Couldn't launch a browser automatically - open it yourself:[/] [link]{url}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Opened [link]{url}[/]");
        return 0;
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            // UseShellExecute-based launch has no single cross-platform guarantee on
            // Linux (no default-browser-launch primitive at the OS level the way
            // Windows/macOS have one) - xdg-open is the de facto standard fallback.
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    Process.Start("xdg-open", url);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
