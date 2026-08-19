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

        var wasAlreadyInitialized = FlareHome.IsInitialized;
        if (!wasAlreadyInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Initializing {FlareHome.Directory}...[/]");
        }

        FlareHome.EnsureInitialized();

        // Skip the port-availability preflight when Flare's own stack is already the
        // thing holding these ports - `docker compose up` against an already-running
        // stack is a normal idempotent no-op, not a conflict to warn about. Otherwise
        // (first-ever init, or a previously-stopped stack), check up front so a collision
        // - most commonly a repo-local `docker compose up` of this same stack, still
        // running (see docs/cli.md's Known limitations) - fails here with a clear
        // per-port message instead of `docker compose up`'s raw bind error.
        var stackAlreadyRunning = wasAlreadyInitialized
            && (await DoctorChecks.CheckStackStateAsync(cancellationToken)).Any(c => c.Passed);

        if (!stackAlreadyRunning)
        {
            var portConflicts = DoctorChecks.CheckPortsAvailable().Where(c => !c.Passed).ToList();
            if (portConflicts.Count > 0)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Port conflict(s) - not starting:");
                foreach (var conflict in portConflicts)
                {
                    AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(conflict.Name)}: {Markup.Escape(conflict.Detail)}");
                }

                return 1;
            }
        }

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

        var port = FlareHome.ReadEnvValue("FLARE_DASHBOARD_PORT", "7777");
        AnsiConsole.MarkupLine($"[green]✓[/] Flare is up: [link]http://localhost:{port}[/]");
        AnsiConsole.MarkupLine("[grey]Auth is off by default - anyone who can reach it has full access. Turn on sign-in from the dashboard's /auth page if you want it. Run `flare open` to launch it.[/]");
        return 0;
    }
}
