using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class StartCommand : AsyncCommand
{
    private static readonly string[] HealthCheckedServices = ["clickhouse", "redis", "ingest", "api"];

    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var preflight = await DoctorChecks.CheckDockerReachableAsync(cancellationToken);
        if (!preflight.Passed)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {preflight.Detail}");
            return 1;
        }

        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Initializing {FlareHome.Directory}...[/]");
        }

        FlareHome.EnsureInitialized();

        AnsiConsole.MarkupLine("[grey]Starting containers (this pulls images on first run)...[/]");
        var upExitCode = await ComposeRunner.RunStreamedAsync(["up", "-d"]);
        if (upExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose up -d` failed - see the output above.");
            return 1;
        }

        var allHealthy = true;
        await AnsiConsole.Status().StartAsync("Waiting for the stack to become healthy...", async ctx =>
        {
            foreach (var service in HealthCheckedServices)
            {
                ctx.Status($"Waiting for [bold]{service}[/] to become healthy...");
                var healthy = await HealthPoller.WaitUntilHealthyAsync(service);
                if (!healthy)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] [bold]{service}[/] did not become healthy in time. Try: [grey]flare logs {service}[/]");
                    allHealthy = false;
                    return;
                }
            }

            // dashboard has no compose-level healthcheck today - "running" is its own
            // actual success bar, not "healthy". Called out explicitly rather than
            // silently treating it the same as the four services above.
            ctx.Status("Waiting for [bold]dashboard[/] to start...");
            var dashboardRunning = await HealthPoller.WaitUntilRunningAsync("dashboard");
            if (!dashboardRunning)
            {
                AnsiConsole.MarkupLine("[red]✗[/] [bold]dashboard[/] did not start in time. Try: [grey]flare logs dashboard[/]");
                allHealthy = false;
            }
        });

        if (!allHealthy)
        {
            return 1;
        }

        var port = FlareHome.ReadEnvValue("FLARE_DASHBOARD_PORT", "3000");
        AnsiConsole.MarkupLine($"[green]✓[/] Flare is up: [link]http://localhost:{port}[/]");
        AnsiConsole.MarkupLine("[grey]Auth is off by default - anyone who can reach it has full access. Turn on sign-in from the dashboard's /auth page if you want it. Run `flare open` to launch it.[/]");
        return 0;
    }
}
