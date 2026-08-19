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

    public static async Task<IReadOnlyList<DiagnosticCheck>> CheckStackStateAsync(CancellationToken ct)
    {
        if (!FlareHome.IsInitialized)
        {
            return [new DiagnosticCheck("Stack initialized", false, "Not initialized yet - run `flare start`.")];
        }

        var services = new[] { "clickhouse", "redis", "ingest", "api", "dashboard" };
        var checks = new List<DiagnosticCheck>(services.Length);

        foreach (var service in services)
        {
            var result = await ComposeRunner.RunCapturedAsync(["ps", "--format", "{{.State}}", service], ct).ConfigureAwait(false);
            var state = result.StandardOutput.Trim();
            checks.Add(new DiagnosticCheck(service, string.Equals(state, "running", StringComparison.OrdinalIgnoreCase), string.IsNullOrEmpty(state) ? "not running" : state));
        }

        return checks;
    }

    /// <summary>
    /// Confirms data is actually flowing, not just that containers report healthy - same
    /// row-count sanity check scripts/diagnose-no-data.sh runs, but against the
    /// container's own CLICKHOUSE_PASSWORD env var (already set via the compose
    /// environment: block) rather than re-parsing ~/.flare/.env on the host side.
    /// </summary>
    public static async Task<DiagnosticCheck> CheckIngestionAsync(CancellationToken ct)
    {
        var result = await ComposeRunner.RunCapturedAsync(
            [
                "exec", "-T", "clickhouse", "sh", "-c",
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

    public static async Task<IReadOnlyList<string>> TailUnhealthyLogsAsync(string service, CancellationToken ct)
    {
        var result = await ComposeRunner.RunCapturedAsync(["logs", "--tail=40", service], ct).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Every host port this stack binds, resolved from ~/.flare/.env (or these same
    /// fallbacks if no .env exists yet, i.e. before the first `flare start`) - the exact
    /// values `docker compose up` would try to bind. Fallbacks mirror env.template's
    /// defaults; keep both in sync if either changes.
    /// </summary>
    private static readonly (string Label, string EnvKey, string Fallback)[] ConfiguredPorts =
    [
        ("ClickHouse HTTP", "CLICKHOUSE_HTTP_PORT", "8123"),
        ("Ingest gRPC (OTLP)", "FLARE_INGEST_GRPC_PORT", "4317"),
        ("Ingest HTTP (OTLP)", "FLARE_INGEST_HTTP_PORT", "4318"),
        ("API", "FLARE_API_PORT", "8080"),
        ("Dashboard", "FLARE_DASHBOARD_PORT", "7777"),
    ];

    /// <summary>
    /// Probes each configured host port with a loopback bind attempt - the same failure
    /// `docker compose up` would hit if something else already owns one of these (most
    /// commonly: a repo-local `docker compose up` of this same stack, still running - see
    /// docs/cli.md's Known limitations), but caught here per-port with a specific message
    /// instead of Compose's one raw "port is already allocated" error for whichever port
    /// it happened to reach first.
    ///
    /// Callers must only invoke this when Flare's OWN stack isn't already the thing
    /// holding these ports - both `flare doctor` and `flare start` skip it whenever the
    /// stack is already running, since a bind failure there just means "our own healthy
    /// containers", not a conflict to report.
    /// </summary>
    public static IReadOnlyList<DiagnosticCheck> CheckPortsAvailable()
    {
        var checks = new List<DiagnosticCheck>(ConfiguredPorts.Length);
        foreach (var (label, envKey, fallback) in ConfiguredPorts)
        {
            var portText = FlareHome.ReadEnvValue(envKey, fallback);
            if (!int.TryParse(portText, out var port))
            {
                checks.Add(new DiagnosticCheck($"Port ({label})", false, $"~/.flare/.env's {envKey}='{portText}' isn't a valid port number."));
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
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return new DiagnosticCheck($"Port {port} ({label})", true, "free");
        }
        catch (SocketException)
        {
            return new DiagnosticCheck(
                $"Port {port} ({label})",
                false,
                "Already in use by something else - change it in ~/.flare/.env, or stop whatever else is bound to it, before starting.");
        }
    }
}
