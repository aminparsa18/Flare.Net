using System.ComponentModel;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class StartCommand : AsyncCommand<StartCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
        [CommandOption("--cluster")]
        [Description("First-init only: stand this instance up as a multi-node ClickHouse cluster (2 shards x 2 replicas + 3-node Keeper quorum) instead of the single-node default - see docs/clustering.md. Ignored (with a mismatch error if it disagrees) once the instance already exists - not a live migration path.")]
        public bool Cluster { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        var topology = settings.Cluster ? FlareTopology.Cluster : FlareTopology.Standalone;

        var preflight = await DoctorChecks.CheckDockerReachableAsync(cancellationToken);
        if (!preflight.Passed)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {preflight.Detail}");
            return 1;
        }

        var wasAlreadyInitialized = instance.IsInitialized;
        if (!wasAlreadyInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]Initializing {instance.Directory}...[/]");
        }

        try
        {
            FlareHome.EnsureInitialized(instance, topology);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var profile = TopologyProfile.For(topology);

        // Skip the port-availability preflight when this instance's own stack is already
        // the thing holding these ports - `docker compose up` against an already-running
        // stack is a normal idempotent no-op, not a conflict to warn about. Otherwise
        // (first-ever init, or a previously-stopped stack), check up front so a collision
        // - most commonly another instance (or a repo-local `docker compose up`) already
        // bound to the same ports - fails here with a clear per-port message instead of
        // `docker compose up`'s raw bind error.
        var stackAlreadyRunning = wasAlreadyInitialized
            && (await DoctorChecks.CheckStackStateAsync(instance, cancellationToken)).Any(c => c.Passed);

        if (!stackAlreadyRunning)
        {
            var portConflicts = DoctorChecks.CheckPortsAvailable(instance, profile.Ports).Where(c => !c.Passed).ToList();
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
        var upExitCode = await ComposeRunner.RunStreamedAsync(instance, ["up", "-d"]);
        if (upExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] `docker compose up -d` failed - see the output above.");
            return 1;
        }

        var allHealthy = true;
        await AnsiConsole.Status().StartAsync("Waiting for the stack to become healthy...", async ctx =>
        {
            foreach (var service in profile.HealthCheckedServices)
            {
                ctx.Status($"Waiting for [bold]{service}[/] to become healthy...");
                var healthy = await HealthPoller.WaitUntilHealthyAsync(instance, service);
                if (!healthy)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] [bold]{service}[/] did not become healthy in time. Try: [grey]flare logs {service}[/]");
                    allHealthy = false;
                    return;
                }
            }

            // RunningOnlyServices (dashboard, both topologies) have no compose-level
            // healthcheck today - "running" is their own actual success bar, not
            // "healthy". Called out explicitly rather than silently treating them the
            // same as HealthCheckedServices above.
            foreach (var service in profile.RunningOnlyServices)
            {
                ctx.Status($"Waiting for [bold]{service}[/] to start...");
                var running = await HealthPoller.WaitUntilRunningAsync(instance, service);
                if (!running)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] [bold]{service}[/] did not start in time. Try: [grey]flare logs {service}[/]");
                    allHealthy = false;
                    return;
                }
            }
        });

        if (!allHealthy)
        {
            return 1;
        }

        var port = instance.ReadEnvValue("FLARE_DASHBOARD_PORT", "7777");
        var instanceSuffix = instance.Name is null ? "" : $" ({instance.Name})";
        AnsiConsole.MarkupLine($"[green]✓[/] Flare{instanceSuffix} is up: [link]http://localhost:{port}[/]");
        AnsiConsole.MarkupLine("[grey]Auth is off by default - anyone who can reach it has full access. Turn on sign-in from the dashboard's /auth page if you want it. Run `flare open` to launch it.[/]");
        return 0;
    }
}
