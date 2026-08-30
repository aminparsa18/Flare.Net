using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Flare.Cli.Internal;

/// <summary>
/// One diagnostic row. <see cref="Detail"/> is the short, always-shown status text (a
/// version, a count, "reachable"); <see cref="SuggestedAction"/> is the longer, only-on-
/// failure "what do I do about this" text - kept as a separate field (rather than folded
/// into Detail, as earlier revisions did) so `flare doctor`'s output can show a clean
/// status column plus a distinct "Suggested action" block for whatever actually failed,
/// instead of every row's Detail being sized for the worst case.
/// </summary>
internal sealed record DiagnosticCheck(string Name, bool Passed, string Detail, string? SuggestedAction = null);

/// <summary>
/// Read-only diagnostic checks shared by `flare doctor` (runs all of them) and
/// `flare start`'s own preflight (runs just the Docker-reachability ones, so `start`
/// fails fast with an actionable message instead of a raw process-launch error).
/// Mirrors scripts/diagnose-no-data.sh's steps, generalized for an environment with no
/// repo checkout present.
/// </summary>
internal static class DoctorChecks
{
    // Short-lived probe client for the HTTP-health checks below (API/Dashboard/OTLP
    // HTTP) - these are one-shot CLI invocations, not a long-running process, so there's
    // no pooled-connection reuse to gain from a static field beyond avoiding a per-call
    // SocketsHttpHandler allocation. A 3s timeout keeps a single dead endpoint from
    // stalling the rest of the report.
    private static readonly HttpClient HttpProbe = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<DiagnosticCheck> CheckDockerReachableAsync(CancellationToken ct)
    {
        try
        {
            var result = await ComposeRunner.RunDockerCapturedAsync(["info"], ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return new DiagnosticCheck("Docker engine", false, "unreachable", "Docker isn't reachable - is Docker Desktop/Engine running?");
            }

            // Best-effort version tag alongside the reachability result - `docker info`
            // above already proved the daemon answers, so a failure here (unexpected
            // output shape on some older engine) just falls back to a plain "reachable"
            // rather than turning a working Docker install into a failed check.
            var versionResult = await ComposeRunner.RunDockerCapturedAsync(["version", "--format", "{{.Server.Version}}"], ct).ConfigureAwait(false);
            var version = versionResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(versionResult.StandardOutput)
                ? versionResult.StandardOutput.Trim()
                : "reachable";
            return new DiagnosticCheck("Docker engine", true, version);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new DiagnosticCheck("Docker engine", false, "not found", "`docker` isn't on PATH - install Docker Desktop/Engine first.");
        }
    }

    public static async Task<DiagnosticCheck> CheckComposePluginAsync(CancellationToken ct)
    {
        var result = await ComposeRunner.RunDockerCapturedAsync(["compose", "version"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return new DiagnosticCheck("Compose", false, "unavailable", "`docker compose` isn't available - the Compose v2 plugin is required.");
        }

        // "Docker Compose version v2.39.1" -> "v2.39.1"; falls back to the raw trimmed
        // output if a future Compose version ever changes that shape.
        var output = result.StandardOutput.Trim();
        var version = output.Split(' ').LastOrDefault(part => part.StartsWith('v')) ?? output;
        return new DiagnosticCheck("Compose", true, version);
    }

    public static async Task<IReadOnlyList<DiagnosticCheck>> CheckStackStateAsync(FlareInstance instance, CancellationToken ct)
    {
        if (!instance.IsInitialized)
        {
            return [new DiagnosticCheck("Stack initialized", false, "not initialized", $"Run `{instance.StartHint}`.")];
        }

        var profile = FlareHome.ResolveTopology(instance);
        var checks = new List<DiagnosticCheck>(profile.HealthCheckedServices.Length + profile.RunningOnlyServices.Length);

        foreach (var service in profile.HealthCheckedServices)
        {
            // {{.Health}} - not {{.State}} - for these: every HealthCheckedServices entry
            // carries a real Docker healthcheck (see Templates/docker-compose*.yml), and
            // reading state instead of health silently downgrades this to "the process
            // launched", missing exactly the "is it actually answering" signal a
            // container stuck reporting unhealthy needs surfaced. Blank output means the
            // container isn't up at all yet (Docker only populates Health once a
            // container exists) - state gives a clearer message than a bare "".
            var result = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.Health}}", service], ct).ConfigureAwait(false);
            var health = result.StandardOutput.Trim();
            if (string.IsNullOrEmpty(health))
            {
                var stateResult = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.State}}", service], ct).ConfigureAwait(false);
                var state = stateResult.StandardOutput.Trim();
                checks.Add(new DiagnosticCheck(service, false, string.IsNullOrEmpty(state) ? "not running" : state, $"`{service}` isn't running - see `flare status` / `flare logs {service}`."));
                continue;
            }

            var healthy = string.Equals(health, "healthy", StringComparison.OrdinalIgnoreCase);
            checks.Add(new DiagnosticCheck(service, healthy, health, healthy ? null : $"`{service}` is reporting {health} - see `flare status` / `flare logs {service}`."));
        }

        foreach (var service in profile.RunningOnlyServices)
        {
            var result = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", "{{.State}}", service], ct).ConfigureAwait(false);
            var state = result.StandardOutput.Trim();
            var running = string.Equals(state, "running", StringComparison.OrdinalIgnoreCase);
            checks.Add(new DiagnosticCheck(service, running, string.IsNullOrEmpty(state) ? "not running" : state, running ? null : $"`{service}` isn't running - see `flare status` / `flare logs {service}`."));
        }

        return checks;
    }

    /// <summary>
    /// Can ClickHouse actually be queried - split out from the row-count check below so
    /// a dead/still-starting ClickHouse reports as a clean "unreachable" instead of a row
    /// count that's misleadingly reported as "0 rows" for the wrong reason.
    /// </summary>
    public static async Task<DiagnosticCheck> CheckClickHouseReachableAsync(FlareInstance instance, CancellationToken ct)
    {
        var execTarget = FlareHome.ResolveTopology(instance).ClickHouseExecTarget;
        var result = await ComposeRunner.RunCapturedAsync(
            instance,
            ["exec", "-T", execTarget, "sh", "-c", "clickhouse-client --password \"$CLICKHOUSE_PASSWORD\" --query \"SELECT 1\""],
            ct).ConfigureAwait(false);

        var reachable = result.ExitCode == 0 && result.StandardOutput.Trim() == "1";
        return new DiagnosticCheck(
            "ClickHouse",
            reachable,
            reachable ? "reachable" : "unreachable",
            reachable ? null : "Couldn't query ClickHouse - see `flare status` / `flare logs clickhouse`.");
    }

    /// <summary>
    /// Confirms data is actually flowing, not just that ClickHouse itself is reachable -
    /// same row-count sanity check scripts/diagnose-no-data.sh runs, but against the
    /// container's own CLICKHOUSE_PASSWORD env var (already set via the compose
    /// environment: block) rather than re-parsing the instance's own .env on the host
    /// side. Callers should only run this after <see cref="CheckClickHouseReachableAsync"/>
    /// has already passed - a failed exec here is presented as "0 rows", which is the
    /// wrong diagnosis when ClickHouse itself is the thing that's down.
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
            return new DiagnosticCheck("ClickHouse data", false, "unknown", "Couldn't query ClickHouse - is it healthy? See `flare status`.");
        }

        var countText = result.StandardOutput.Trim();
        var hasRows = long.TryParse(countText, out var count) && count > 0;
        return new DiagnosticCheck(
            "ClickHouse data",
            hasRows,
            hasRows ? $"{count:N0} row(s)" : "0 rows",
            hasRows ? null : "Point a logger at the OTLP endpoint, see `flare logs ingest` if you expected data already.");
    }

    /// <summary>
    /// Same protocol-level ping the redis container's own compose healthcheck already
    /// runs, surfaced here as its own row rather than folded into the generic
    /// container-health loop above, matching how ClickHouse gets a dedicated reachability
    /// row rather than just its container-Health status.
    /// </summary>
    public static async Task<DiagnosticCheck> CheckRedisReachableAsync(FlareInstance instance, CancellationToken ct)
    {
        var result = await ComposeRunner.RunCapturedAsync(
            instance,
            ["exec", "-T", "redis", "sh", "-c", "redis-cli -a \"$REDIS_PASSWORD\" --no-auth-warning ping"],
            ct).ConfigureAwait(false);

        var reply = result.StandardOutput.Trim();
        var reachable = result.ExitCode == 0 && string.Equals(reply, "PONG", StringComparison.Ordinal);
        return new DiagnosticCheck(
            "Redis",
            reachable,
            reachable ? "reachable" : "unreachable",
            reachable ? null : "Couldn't reach Redis - see `flare status` / `flare logs redis`.");
    }

    /// <summary>
    /// GET a host-published HTTP endpoint and treat any non-5xx response as healthy - the
    /// point is confirming something is actually answering on that port (not necessarily
    /// that every downstream dependency of that service is itself healthy, which the
    /// container-health checks above already cover for the services that have one).
    /// </summary>
    public static async Task<DiagnosticCheck> CheckHttpHealthAsync(string name, string url, CancellationToken ct)
    {
        try
        {
            using var response = await HttpProbe.GetAsync(url, ct).ConfigureAwait(false);
            var healthy = (int)response.StatusCode < 500;
            return new DiagnosticCheck(
                name,
                healthy,
                healthy ? "healthy" : $"HTTP {(int)response.StatusCode}",
                healthy ? null : $"{url} returned HTTP {(int)response.StatusCode} - see `flare status` / `flare logs`.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new DiagnosticCheck(name, false, "unreachable", $"Couldn't reach {url} - see `flare status`.");
        }
    }

    /// <summary>
    /// Bare TCP connect to a host-published port - used for the OTLP gRPC endpoint,
    /// which has no plain-HTTP health route to GET. A successful connect (then
    /// immediately dropped) is enough to say "something is listening here"; it
    /// deliberately doesn't attempt a gRPC handshake.
    /// </summary>
    public static async Task<DiagnosticCheck> CheckTcpListeningAsync(string name, int port, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return new DiagnosticCheck(name, true, $"listening on :{port}");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new DiagnosticCheck(name, false, "not listening", $"Nothing is listening on :{port} - see `flare status` / `flare logs ingest`.");
        }
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
                checks.Add(new DiagnosticCheck($"Port ({label})", false, $"invalid ('{portText}')", $"{instance.EnvFilePath}'s {envKey}='{portText}' isn't a valid port number."));
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
                "in use",
                "Already in use by something else - change it in .env, or stop whatever else is bound to it, before starting.");
        }
    }
}
