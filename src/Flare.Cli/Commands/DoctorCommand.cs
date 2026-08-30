using System.Globalization;
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

        AnsiConsole.MarkupLine("[bold]Flare Doctor[/]");
        AnsiConsole.MarkupLine(new string('─', 32));

        var dockerCheck = await DoctorChecks.CheckDockerReachableAsync(cancellationToken);
        Report(dockerCheck, ref allPassed);
        if (!dockerCheck.Passed)
        {
            // Nothing else below can succeed without Docker reachable - stop here
            // rather than printing a wall of downstream failures for one root cause.
            return Finish(allPassed);
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
            ReportPortGroup(DoctorChecks.CheckPortsAvailable(instance, profile.Ports), ref allPassed);

            return Finish(allPassed);
        }

        var stackChecks = await DoctorChecks.CheckStackStateAsync(instance, cancellationToken);
        ReportContainerGroup(stackChecks, ref allPassed);
        foreach (var check in stackChecks.Where(c => !c.Passed))
        {
            var tail = await DoctorChecks.TailUnhealthyLogsAsync(instance, check.Name, cancellationToken);
            foreach (var line in tail)
            {
                AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(line)}[/]");
            }
        }

        // Only meaningful while the stack is down - if any service is already running,
        // it's this instance's own containers holding these ports, not a conflict.
        if (!stackChecks.Any(c => c.Passed))
        {
            ReportPortGroup(DoctorChecks.CheckPortsAvailable(instance, profile.Ports), ref allPassed);
        }

        if (stackChecks.All(c => c.Passed))
        {
            Report(await DoctorChecks.CheckClickHouseReachableAsync(instance, cancellationToken), ref allPassed);
            Report(await DoctorChecks.CheckIngestionAsync(instance, cancellationToken), ref allPassed);
            Report(await DoctorChecks.CheckRedisReachableAsync(instance, cancellationToken), ref allPassed);

            var apiPort = ResolvePort(instance, profile, "API");
            Report(await DoctorChecks.CheckHttpHealthAsync("API", $"http://localhost:{apiPort}/health", cancellationToken), ref allPassed);

            var dashboardPort = ResolvePort(instance, profile, "Dashboard");
            Report(await DoctorChecks.CheckHttpHealthAsync("Dashboard", $"http://localhost:{dashboardPort}/", cancellationToken), ref allPassed);

            foreach (var (label, envKey, fallback) in profile.Ports.Where(p => p.Label.Contains("OTLP", StringComparison.OrdinalIgnoreCase)))
            {
                var port = ResolvePort(instance, envKey, fallback);
                var check = label.Contains("gRPC", StringComparison.OrdinalIgnoreCase)
                    ? await DoctorChecks.CheckTcpListeningAsync(label, port, cancellationToken)
                    : await DoctorChecks.CheckHttpHealthAsync(label, $"http://localhost:{port}/health", cancellationToken);
                Report(check, ref allPassed);
            }
        }

        return Finish(allPassed);
    }

    private static int ResolvePort(FlareInstance instance, TopologyProfile profile, string labelContains)
    {
        var (_, envKey, fallback) = profile.Ports.First(p => p.Label.Contains(labelContains, StringComparison.OrdinalIgnoreCase));
        return ResolvePort(instance, envKey, fallback);
    }

    private static int ResolvePort(FlareInstance instance, string envKey, int fallback)
    {
        var text = instance.ReadEnvValue(envKey, fallback.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(text, out var port) ? port : fallback;
    }

    /// <summary>
    /// Collapses the per-port checks to a single "Ports" row when everything's free -
    /// nobody needs 5 individual "✓ free" lines to know the stack can start - but expands
    /// back to one row per offending port (with its own suggested action) the moment
    /// anything's actually taken, since that's exactly the moment the detail matters.
    /// </summary>
    private static void ReportPortGroup(IReadOnlyList<DiagnosticCheck> portChecks, ref bool allPassed)
    {
        if (portChecks.All(c => c.Passed))
        {
            Report(new DiagnosticCheck("Ports", true, $"{portChecks.Count}/{portChecks.Count} available"), ref allPassed);
            return;
        }

        var failing = portChecks.Count(c => !c.Passed);
        Report(new DiagnosticCheck("Ports", false, $"{failing}/{portChecks.Count} unavailable"), ref allPassed);
        foreach (var check in portChecks.Where(c => !c.Passed))
        {
            Report(check, ref allPassed, indent: true);
        }
    }

    /// <summary>Same collapse-when-clean, expand-when-not treatment as <see cref="ReportPortGroup"/>, for container state.</summary>
    private static void ReportContainerGroup(IReadOnlyList<DiagnosticCheck> stackChecks, ref bool allPassed)
    {
        if (stackChecks.All(c => c.Passed))
        {
            Report(new DiagnosticCheck("Containers", true, $"{stackChecks.Count}/{stackChecks.Count} running"), ref allPassed);
            return;
        }

        var running = stackChecks.Count(c => c.Passed);
        Report(new DiagnosticCheck("Containers", false, $"{running}/{stackChecks.Count} running"), ref allPassed);
        foreach (var check in stackChecks.Where(c => !c.Passed))
        {
            Report(check, ref allPassed, indent: true);
        }
    }

    private static void Report(DiagnosticCheck check, ref bool allPassed, bool indent = false)
    {
        var icon = check.Passed ? "[green]✓[/]" : "[red]✗[/]";
        var prefix = indent ? "  " : "";
        AnsiConsole.MarkupLine($"{prefix}{icon} {Markup.Escape(check.Name),-24} {Markup.Escape(check.Detail)}");
        if (!check.Passed && check.SuggestedAction is { } action)
        {
            AnsiConsole.MarkupLine($"{prefix}    [grey]{Markup.Escape(action)}[/]");
        }

        allPassed &= check.Passed;
    }

    private static int Finish(bool allPassed)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(allPassed ? "[bold green]Result: HEALTHY[/]" : "[bold red]Result: UNHEALTHY[/]");
        return allPassed ? 0 : 1;
    }
}
