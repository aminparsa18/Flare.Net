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
                SyntheticMonitorKind.Dns => await ProbeDnsAsync(monitor, started, timeout.Token),
                SyntheticMonitorKind.Udp => await ProbeUdpAsync(monitor, started, timeout.Token),
                SyntheticMonitorKind.Icmp => await ProbeIcmpAsync(monitor, started, timeout.Token),
                _ => await ProbeTlsAsync(monitor, started, timeout.Token),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(started, $"timed out after {monitor.TimeoutSeconds}s");
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or System.Net.NetworkInformation.PingException or System.Security.Authentication.AuthenticationException or InvalidOperationException)
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
        if (monitor.RequestBody.Length > 0)
        {
            request.Content = new StringContent(monitor.RequestBody, System.Text.Encoding.UTF8);
            request.Content.Headers.ContentType = null; // a configured Content-Type header decides; otherwise none
        }

        foreach (var (name, value) in SyntheticHeaders.Parse(monitor.RequestHeaders).Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var assertsBody = monitor.BodyContains.Length > 0 || monitor.BodyNotContains.Length > 0
            || monitor.BodyMatchesRegex.Length > 0 || monitor.JsonPath.Length > 0;
        // Headers only unless a body assertion needs the body: the probe measures time to first response, and
        // must not download a large body for nothing.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var status = (int)response.StatusCode;
        if (!StatusMatches(status, monitor.ExpectedStatus))
        {
            return new SyntheticProbeResult(false, ElapsedMs(started), status, null, $"unexpected status {status}");
        }

        string? error = null;
        if (assertsBody)
        {
            var body = await ReadBodyAsync(response, cancellationToken);
            error = BodyAssertionError(body, monitor.BodyContains, monitor.BodyNotContains)
                ?? SyntheticAssertions.Error(body, monitor.BodyMatchesRegex, monitor.JsonPath, monitor.JsonPathEquals);
        }

        return new SyntheticProbeResult(error is null, ElapsedMs(started), status, null, error);
    }

    /// <summary>Maximum bytes of a response body read for assertions.</summary>
    public const int MaxBodyBytes = 1_048_576;

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxBodyBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>The failure reason when <paramref name="body"/> breaks an assertion, else null. Case-sensitive substring checks.</summary>
    public static string? BodyAssertionError(string body, string contains, string notContains)
    {
        if (contains.Length > 0 && !body.Contains(contains, StringComparison.Ordinal))
        {
            return "response body does not contain the expected text";
        }

        return notContains.Length > 0 && body.Contains(notContains, StringComparison.Ordinal)
            ? "response body contains the forbidden text"
            : null;
    }

    private static async Task<SyntheticProbeResult> ProbeTcpAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        var (host, port) = SyntheticTarget.ParseHostPort(monitor.Target, null) ?? throw new InvalidOperationException("invalid target");
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken);
        return new SyntheticProbeResult(true, ElapsedMs(started), null, null, null);
    }

    private static async Task<SyntheticProbeResult> ProbeDnsAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        if (SyntheticDnsWire.TypeFor(monitor.Method) is { } type)
        {
            var records = await SyntheticDnsWire.ResolveAsync(monitor.Target.Trim(), type, cancellationToken);
            if (records.Count == 0)
            {
                return new SyntheticProbeResult(false, ElapsedMs(started), null, null, $"no {monitor.Method.ToUpperInvariant()} records returned");
            }

            var wanted = monitor.ExpectedAnswer.Trim().TrimEnd('.');
            if (wanted.Length > 0 && !records.Any(r => r.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
            {
                return new SyntheticProbeResult(false, ElapsedMs(started), null, null, $"answer {string.Join(" | ", records)} does not include {wanted}");
            }

            return new SyntheticProbeResult(true, ElapsedMs(started), null, null, null);
        }

        var family = monitor.Method.Equals("AAAA", StringComparison.OrdinalIgnoreCase) ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var addresses = (await System.Net.Dns.GetHostAddressesAsync(monitor.Target.Trim(), family, cancellationToken)).ToList();
        if (addresses.Count == 0)
        {
            return new SyntheticProbeResult(false, ElapsedMs(started), null, null, "no addresses returned");
        }

        if (System.Net.IPAddress.TryParse(monitor.ExpectedAnswer, out var expected) && !addresses.Contains(expected))
        {
            return new SyntheticProbeResult(false, ElapsedMs(started), null, null, $"answer {string.Join(", ", addresses)} does not include {expected}");
        }

        return new SyntheticProbeResult(true, ElapsedMs(started), null, null, null);
    }

    private static async Task<SyntheticProbeResult> ProbeUdpAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        var (host, port) = SyntheticTarget.ParseHostPort(monitor.Target, null) ?? throw new InvalidOperationException("invalid target");
        using var udp = new UdpClient();
        udp.Connect(host, port); // connected, so an ICMP port-unreachable surfaces as a SocketException
        await udp.SendAsync(System.Text.Encoding.UTF8.GetBytes(monitor.RequestBody), cancellationToken);
        var reply = await udp.ReceiveAsync(cancellationToken);
        if (monitor.ExpectedAnswer.Length > 0
            && !System.Text.Encoding.UTF8.GetString(reply.Buffer).Contains(monitor.ExpectedAnswer, StringComparison.Ordinal))
        {
            return new SyntheticProbeResult(false, ElapsedMs(started), null, null, "reply does not contain the expected text");
        }

        return new SyntheticProbeResult(true, ElapsedMs(started), null, null, null);
    }

    private static async Task<SyntheticProbeResult> ProbeIcmpAsync(SyntheticMonitor monitor, long started, CancellationToken cancellationToken)
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var reply = await ping.SendPingAsync(monitor.Target.Trim(), TimeSpan.FromSeconds(monitor.TimeoutSeconds), cancellationToken: cancellationToken);
        return reply.Status == System.Net.NetworkInformation.IPStatus.Success
            ? new SyntheticProbeResult(true, ElapsedMs(started), null, null, null)
            : new SyntheticProbeResult(false, ElapsedMs(started), null, null, $"ping failed: {reply.Status}");
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
