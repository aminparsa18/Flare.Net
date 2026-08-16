using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

internal sealed class DoctorCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
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

        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]○[/] Stack not initialized yet - run `flare start`. (not an error)");
            return allPassed ? 0 : 1;
        }

        var stackChecks = await DoctorChecks.CheckStackStateAsync(cancellationToken);
        foreach (var check in stackChecks)
        {
            Report(check, ref allPassed);
            if (!check.Passed)
            {
                var tail = await DoctorChecks.TailUnhealthyLogsAsync(check.Name, cancellationToken);
                foreach (var line in tail)
                {
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(line)}[/]");
                }
            }
        }

        if (stackChecks.All(c => c.Passed))
        {
            var ingestionCheck = await DoctorChecks.CheckIngestionAsync(cancellationToken);
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
