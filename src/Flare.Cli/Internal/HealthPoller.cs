namespace Flare.Cli.Internal;

/// <summary>
/// Ports scripts/run-full-stack.sh's health-poll loop (2s interval, ~120s cap) from the
/// Flare.Net repo so `flare start` gives the same "wait for the stack to actually be
/// ready" behavior that script already established, without needing that script or a
/// repo checkout present.
/// </summary>
internal static class HealthPoller
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Polls `docker compose ps --format '{{.Health}}' &lt;service&gt;` until it reports
    /// "healthy" or the timeout elapses. Use for services with a compose-level
    /// healthcheck (clickhouse/redis/ingest/api).
    /// </summary>
    public static Task<bool> WaitUntilHealthyAsync(FlareInstance instance, string service, CancellationToken ct = default, TimeSpan? timeout = null)
        => PollAsync(instance, service, "{{.Health}}", "healthy", timeout ?? DefaultTimeout, ct);

    /// <summary>
    /// Polls `docker compose ps --format '{{.State}}' &lt;service&gt;` until it reports
    /// "running" or the timeout elapses. `dashboard` has no compose-level healthcheck
    /// today (see Templates/docker-compose.flare.yml), so "running" is its actual
    /// success bar - callers should surface that asymmetry rather than hide it.
    /// </summary>
    public static Task<bool> WaitUntilRunningAsync(FlareInstance instance, string service, CancellationToken ct = default, TimeSpan? timeout = null)
        => PollAsync(instance, service, "{{.State}}", "running", timeout ?? DefaultTimeout, ct);

    private static async Task<bool> PollAsync(FlareInstance instance, string service, string format, string expected, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var result = await ComposeRunner.RunCapturedAsync(instance, ["ps", "--format", format, service], ct).ConfigureAwait(false);
            var value = result.StandardOutput.Trim();

            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }

        return false;
    }
}
