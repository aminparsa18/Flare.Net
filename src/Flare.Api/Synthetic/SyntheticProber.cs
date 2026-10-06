using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using Flare.Api.Model;

namespace Flare.Api.Synthetic;

/// <summary>The outcome of one probe. <see cref="CertExpiryDays"/> is set only when a certificate was seen.</summary>
public sealed record SyntheticProbeResult(bool Up, double DurationMs, int? HttpStatus, double? CertExpiryDays, string? Error);

/// <summary>
/// Runs one probe of a <see cref="SyntheticMonitor"/>. Never throws for a failed probe: any connect, TLS,
/// timeout or HTTP failure is a result with <c>Up = false</c> and the reason in <c>Error</c>; only caller
/// cancellation propagates. See <c>docs-internal/adr/0128-synthetic-monitoring.md</c>.
/// </summary>
public sealed class SyntheticProber(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
{
    public const string HttpClientName = "synthetic-probe";

    public async Task<SyntheticProbeResult> ProbeAsync(SyntheticMonitor monitor, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(monitor.TimeoutSeconds));
        try
        {
            return monitor.Kind switch
            {
                SyntheticMonitorKind.Http => await ProbeHttpAsync(monitor, started, timeout.Token),
                SyntheticMonitorKind.Tcp => await ProbeTcpAsync(monitor, started, timeout.Token),
                _ => await ProbeTlsAsync(monitor, started, timeout.Token),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(started, $"timed out after {monitor.TimeoutSeconds}s");
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or System.Security.Authentication.AuthenticationException or InvalidOperationException)
        {
            return Failed(started, ex.GetBaseException().Message);
        }
    }

    /// <summary>Whether <paramref name="status"/> counts as up for <paramref name="expected"/> (0 = any 2xx or 3xx).</summary>
    public static bool StatusMatches(int status, int expected) => expected == 0 ? status is >= 200 and < 400 : status == expected;

    private async Task<SyntheticProbeResult> ProbeHttpAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(new HttpMethod(monitor.Method), monitor.Target);
        // Headers only: the probe measures time to first response, and must not download a large body.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var status = (int)response.StatusCode;
        var up = StatusMatches(status, monitor.ExpectedStatus);
        return new SyntheticProbeResult(up, ElapsedMs(started), status, null, up ? null : $"unexpected status {status}");
    }

    private static async Task<SyntheticProbeResult> ProbeTcpAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        var (host, port) = SyntheticTarget.ParseHostPort(monitor.Target, null) ?? throw new InvalidOperationException("invalid target");
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken);
        return new SyntheticProbeResult(true, ElapsedMs(started), null, null, null);
    }

    private async Task<SyntheticProbeResult> ProbeTlsAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        var (host, port) = SyntheticTarget.ParseHostPort(monitor.Target, 443) ?? throw new InvalidOperationException("invalid target");
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken);
        await using var ssl = new SslStream(tcp.GetStream());
        // The default validation applies: an untrusted, expired or mismatched certificate fails the handshake,
        // which is the "down" this monitor exists to report.
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cancellationToken);
        double? expiry = null;
        if (ssl.RemoteCertificate is { } cert)
        {
            using var x509 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);
            expiry = ExpiryDays(x509.NotAfter.ToUniversalTime(), timeProvider.GetUtcNow());
        }

        return new SyntheticProbeResult(true, ElapsedMs(started), null, expiry, null);
    }

    /// <summary>Days from <paramref name="now"/> until <paramref name="notAfterUtc"/>; negative once expired.</summary>
    public static double ExpiryDays(DateTime notAfterUtc, DateTimeOffset now) =>
        Math.Round((new DateTimeOffset(DateTime.SpecifyKind(notAfterUtc, DateTimeKind.Utc)) - now).TotalDays, 2);

    private static SyntheticProbeResult Failed(long started, string error) => new(false, ElapsedMs(started), null, null, error);

    private static double ElapsedMs(long started) => Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2);
}
