using System.Net;
using System.Net.Sockets;

namespace Flare.Cli.Internal;

internal sealed record DiagnosticCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Read-only diagnostic checks shared by `flare doctor` (runs all of them) and
/// `flare start`'s own preflight (runs just the Docker-reachability ones, so `start`
/// fails fast with an actionable message instead of a raw process-launch error).
/// Mirrors scripts/diagnose-no-data.sh's steps, generalized for an environment with no
/// repo checkout present.
/// </summary>
internal static class DoctorChecks
{
    public static async Task<DiagnosticCheck> CheckDockerReachableAsync(CancellationToken ct)
    {
        try
        {
            var result = await ComposeRunner.RunDockerCapturedAsync(["info"], ct).ConfigureAwait(false);
            return new DiagnosticCheck(
                "Docker daemon reachable",
                result.ExitCode == 0,
                result.ExitCode == 0 ? "OK" : "Docker isn't reachable - is Docker Desktop/Engine running?");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new DiagnosticCheck("Docker daemon reachable", false, "`docker` isn't on PATH - install Docker Desktop/Engine first.");
        }
    }

    public static async Task<DiagnosticCheck> CheckComposePluginAsync(CancellationToken ct)
    {
        var result = await ComposeRunner.RunDockerCapturedAsync(["compose", "version"], ct).ConfigureAwait(false);
        return new DiagnosticCheck(
            "Docker Compose v2 plugin present",
            result.ExitCode == 0,
            result.ExitCode == 0 ? result.StandardOutput.Trim() : "`docker compose` isn't available - the Compose v2 plugin is required.");
    }

    public static async Task<IReadOnlyList<DiagnosticCheck>> CheckStackStateAsync(FlareInstance instance, CancellationToken ct)
    {
        if (!instance.IsInitialized)
        {
            return [new DiagnosticCheck("Stack initialized", false, "Not initialized yet - run `flare start`.")];
        }

        var profile = FlareHome.ResolveTopology(instance);
        var services = profile.HealthCheckedServices.Concat(profile.RunningOnlyServices).ToArray();
        var checks = new List<DiagnosticCheck>(services.Length);

        foreach (var service in services)
        {
            var result = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.State}}", service], ct).ConfigureAwait(false);
            var state = result.StandardOutput.Trim();
            checks.Add(new DiagnosticCheck(service, string.Equals(state, "running", StringComparison.OrdinalIgnoreCase), string.IsNullOrEmpty(state) ? "not running" : state));
        }

        return checks;
    }

    /// <summary>
    /// Confirms data is actually flowing, not just that containers report healthy - same
    /// row-count sanity check scripts/diagnose-no-data.sh runs, but against the
    /// container's own CLICKHOUSE_PASSWORD env var (already set via the compose
    /// environment: block) rather than re-parsing the instance's own .env on the host
    /// side.
    /// </summary>
    public static async Task<DiagnosticCheck> CheckIngestionAsync(FlareInstance instance, CancellationToken ct)
    {
        var execTarget = FlareHome.ResolveTopology(instance).ClickHouseExecTarget;
        var result = await ComposeRunner.RunCapturedAsync(
            instance,
            [
                "exec", "-T", execTarget, "sh", "-c",
                "clickhouse-client --password \"$CLICKHOUSE_PASSWORD\" --query \"SELECT count() FROM clickhousedb.logs\"",
            ],
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new DiagnosticCheck("Log rows in ClickHouse", false, "Couldn't query ClickHouse - is it healthy? See `flare status`.");
        }

        var countText = result.StandardOutput.Trim();
        var hasRows = long.TryParse(countText, out var count) && count > 0;
        return new DiagnosticCheck(
            "Log rows in ClickHouse",
            hasRows,
            hasRows ? $"{count} row(s)" : "0 rows - point a logger at the OTLP endpoint, see `flare logs ingest` if you expected data already.");
    }

    public static async Task<IReadOnlyList<string>> TailUnhealthyLogsAsync(FlareInstance instance, string service, CancellationToken ct)
    {
        var result = await ComposeRunner.RunCapturedAsync(instance, ["logs", "--tail=40", service], ct).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Probes each configured host port with a loopback bind attempt - the same failure
    /// `docker compose up` would hit if something else already owns one of these, but
    /// caught here per-port with a specific message instead of Compose's one raw
    /// "port is already allocated" error for whichever port it happened to reach first.
    ///
    /// Callers must only invoke this when the given instance's OWN stack isn't already
    /// the thing holding these ports - both `flare doctor` and `flare start` skip it
    /// whenever that instance is already running, since a bind failure there just means
    /// "our own healthy containers", not a conflict to report.
    /// </summary>
    public static IReadOnlyList<DiagnosticCheck> CheckPortsAvailable(FlareInstance instance, (string Label, string EnvKey, int Fallback)[] portDefaults)
    {
        var checks = new List<DiagnosticCheck>(portDefaults.Length);
        foreach (var (label, envKey, fallback) in portDefaults)
        {
            var portText = instance.ReadEnvValue(envKey, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!int.TryParse(portText, out var port))
            {
                checks.Add(new DiagnosticCheck($"Port ({label})", false, $"{instance.EnvFilePath}'s {envKey}='{portText}' isn't a valid port number."));
                continue;
            }

            checks.Add(CheckPortAvailable(label, port));
        }

        return checks;
    }

    private static DiagnosticCheck CheckPortAvailable(string label, int port)
    {
        try
        {
            // IPAddress.Any (0.0.0.0), not Loopback: Docker publishes container ports
            // bound to all interfaces, and a Loopback-only bind check can come back
            // "free" even when Docker already holds the port - confirmed live on macOS
            // (Docker Desktop's port-forwarding proxy listens dual-stack; a same-port
            // IPv4-loopback-only TcpListener.Start() bound successfully alongside it,
            // then `docker compose up` failed with its own raw "port is already
            // allocated" error - exactly the case this check exists to catch instead).
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return new DiagnosticCheck($"Port {port} ({label})", true, "free");
        }
        catch (SocketException)
        {
            return new DiagnosticCheck(
                $"Port {port} ({label})",
                false,
                "Already in use by something else - change it in .env, or stop whatever else is bound to it, before starting.");
        }
    }
}
