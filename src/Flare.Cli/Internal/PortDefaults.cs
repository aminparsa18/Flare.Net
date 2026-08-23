namespace Flare.Cli.Internal;

/// <summary>
/// Single source of truth for the host ports a Flare stack binds and their documented
/// defaults - previously duplicated three ways (env.template's literals, DoctorChecks'
/// own <c>ConfiguredPorts</c>, StatusCommand's own local array), each with a comment
/// asking humans to keep the others in sync by hand. Keep this in sync with
/// Templates/docker-compose.flare.yml's port bindings if either changes.
/// </summary>
internal static class PortDefaults
{
    public static readonly (string Label, string EnvKey, int Fallback)[] All =
    [
        ("ClickHouse HTTP", "CLICKHOUSE_HTTP_PORT", 8123),
        ("Ingest gRPC (OTLP)", "FLARE_INGEST_GRPC_PORT", 4317),
        ("Ingest HTTP (OTLP)", "FLARE_INGEST_HTTP_PORT", 4318),
        ("API", "FLARE_API_PORT", 8080),
        ("Dashboard", "FLARE_DASHBOARD_PORT", 7777),
    ];

    /// <summary>
    /// For a brand-new named instance: the first free host port at or after each
    /// service's documented default, so a second/third stack on the same machine doesn't
    /// need any hand-editing before its first `flare start`. Probed with the same
    /// loopback-bind technique <see cref="DoctorChecks"/> uses to detect conflicts - used
    /// here to pick around one instead of just reporting it. `flare start` still runs the
    /// usual pre-flight port check right before `docker compose up`, so a probe/use race
    /// here is an existing, already-handled failure mode, not a new one. The default
    /// instance never calls this - it always keeps the static defaults above.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ProbeFreePorts()
    {
        var resolved = new Dictionary<string, int>(All.Length);

        // Tracks ports this pass has already handed to an earlier entry - each
        // individual IsFree() check binds-then-immediately-releases, so without this a
        // later port (e.g. FLARE_INGEST_HTTP_PORT probing from 4318) can land on the
        // exact same free port an earlier one (FLARE_INGEST_GRPC_PORT, probing from
        // 4317) was JUST assigned, since nothing is still holding it open in between.
        // Confirmed live: both landed on 4319 without this guard, and `docker compose
        // up` then failed trying to bind the same host port twice for one container.
        var claimedThisPass = new HashSet<int>();
        foreach (var (_, envKey, fallback) in All)
        {
            var port = FindFreePort(fallback, claimedThisPass);
            claimedThisPass.Add(port);
            resolved[envKey] = port;
        }

        return resolved;
    }

    private static int FindFreePort(int startingAt, HashSet<int> claimedThisPass)
    {
        const int MaxAttempts = 1000;
        for (var port = startingAt; port < startingAt + MaxAttempts; port++)
        {
            if (!claimedThisPass.Contains(port) && IsFree(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"Couldn't find a free port at or after {startingAt} after {MaxAttempts} attempts.");
    }

    private static bool IsFree(int port)
    {
        try
        {
            // IPAddress.Any (0.0.0.0), not Loopback: Docker publishes container ports
            // bound to all interfaces, and a Loopback-only bind check can come back
            // "free" even when Docker already holds the port - confirmed live on macOS
            // (Docker Desktop's port-forwarding proxy listens dual-stack; a same-port
            // IPv4-loopback-only TcpListener.Start() bound successfully alongside it).
            // Binding Any is what actually collides with Docker's own bind attempt.
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }
}
