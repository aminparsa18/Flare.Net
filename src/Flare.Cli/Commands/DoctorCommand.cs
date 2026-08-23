using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class DoctorCommand : AsyncCommand<DoctorCommand.Settings>
{
    internal sealed class Settings : InstanceSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var instance = FlareHome.ResolveTarget(settings.InstanceName);
        var allPassed = true;

        var dockerCheck = await DoctorChecks.CheckDockerReachableAsync(cancellationToken);
        Report(dockerCheck, ref allPassed);
        if (!dockerCheck.Passed)
        {
            // Nothing else below can succeed without Docker reachable - stop here
            // rather than printing a wall of downstream failures for one root cause.
            return 1;
        }

        var composeCheck = await DoctorChecks.CheckComposePluginAsync(cancellationToken);
        Report(composeCheck, ref allPassed);

        // Standalone default pre-init (nothing persisted yet to say otherwise - `flare
        // doctor` has no --cluster flag of its own, that's `flare start`'s call). Once
        // initialized this reads the instance's own persisted topology.
        var profile = FlareHome.ResolveTopology(instance);

        if (!instance.IsInitialized)
        {
            AnsiConsole.MarkupLine($"[grey]○[/] Stack not initialized yet - run `{instance.StartHint}`. (not an error)");

            // Worth checking even pre-init: this is exactly the moment a port conflict
            // (e.g. another instance, or a repo-local `docker compose up`, already bound
            // to the same ports) is most useful to catch, before the first `flare start`
            // hits it as a raw Docker bind error instead.
            foreach (var portCheck in DoctorChecks.CheckPortsAvailable(instance, profile.Ports))
            {
                Report(portCheck, ref allPassed);
            }

            return allPassed ? 0 : 1;
        }

        var stackChecks = await DoctorChecks.CheckStackStateAsync(instance, cancellationToken);
        foreach (var check in stackChecks)
        {
            Report(check, ref allPassed);
            if (!check.Passed)
            {
                var tail = await DoctorChecks.TailUnhealthyLogsAsync(instance, check.Name, cancellationToken);
                foreach (var line in tail)
                {
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(line)}[/]");
                }
            }
        }

        // Only meaningful while the stack is down - if any service is already running,
        // it's this instance's own containers holding these ports, not a conflict.
        if (!stackChecks.Any(c => c.Passed))
        {
            foreach (var portCheck in DoctorChecks.CheckPortsAvailable(instance, profile.Ports))
            {
                Report(portCheck, ref allPassed);
            }
        }

        if (stackChecks.All(c => c.Passed))
        {
            var ingestionCheck = await DoctorChecks.CheckIngestionAsync(instance, cancellationToken);
            Report(ingestionCheck, ref allPassed);
        }

        return allPassed ? 0 : 1;
    }

    private static void Report(DiagnosticCheck check, ref bool allPassed)
    {
        var icon = check.Passed ? "[green]✓[/]" : "[red]✗[/]";
        AnsiConsole.MarkupLine($"{icon} {Markup.Escape(check.Name)}: {Markup.Escape(check.Detail)}");
        allPassed &= check.Passed;
    }
}
