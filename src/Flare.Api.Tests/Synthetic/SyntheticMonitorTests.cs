using System.Net;
using System.Net.Sockets;
using Flare.Api.Model;
using Flare.Api.Synthetic;
using Xunit;

namespace Flare.Api.Tests.Synthetic;

public class SyntheticMonitorTests
{
    [Theory]
    [InlineData("example.com:443", null, "example.com", 443)]
    [InlineData("example.com", 443, "example.com", 443)]
    [InlineData("[::1]:8443", null, "::1", 8443)]
    [InlineData("[::1]", 443, "::1", 443)]
    [InlineData(" db.internal:5432 ", null, "db.internal", 5432)]
    public void ParseHostPort_accepts(string target, int? defaultPort, string host, int port) =>
        Assert.Equal((host, port), SyntheticTarget.ParseHostPort(target, defaultPort));

    [Theory]
    [InlineData("example.com", null)]
    [InlineData(":443", null)]
    [InlineData("example.com:0", null)]
    [InlineData("example.com:70000", null)]
    [InlineData("example.com:abc", null)]
    [InlineData("::1", 443)]
    [InlineData("[::1", 443)]
    [InlineData("https://example.com:443", null)]
    [InlineData("a b:80", null)]
    public void ParseHostPort_rejects(string target, int? defaultPort) =>
        Assert.Null(SyntheticTarget.ParseHostPort(target, defaultPort));

    [Fact]
    public void Validate_accepts_a_minimal_http_monitor() =>
        Assert.Null(new SyntheticMonitorRequest { Name = "site", Target = "https://example.com/health" }.Validate());

    [Theory]
    [InlineData("", "https://example.com", SyntheticMonitorKind.Http)]
    [InlineData("n", "ftp://example.com", SyntheticMonitorKind.Http)]
    [InlineData("n", "example.com", SyntheticMonitorKind.Http)]
    [InlineData("n", "example.com", SyntheticMonitorKind.Tcp)]
    [InlineData("n", "", SyntheticMonitorKind.Tls)]
    public void Validate_rejects_bad_targets(string name, string target, SyntheticMonitorKind kind) =>
        Assert.NotNull(new SyntheticMonitorRequest { Name = name, Target = target, Kind = kind }.Validate());

    [Fact]
    public void Validate_tls_target_may_omit_the_port() =>
        Assert.Null(new SyntheticMonitorRequest { Name = "cert", Target = "example.com", Kind = SyntheticMonitorKind.Tls }.Validate());

    [Theory]
    [InlineData(9, 5)]
    [InlineData(86_401, 5)]
    [InlineData(60, 0)]
    [InlineData(60, 121)]
    [InlineData(20, 30)]
    public void Validate_rejects_bad_timing(int interval, int timeout) =>
        Assert.NotNull(new SyntheticMonitorRequest { Name = "n", Target = "https://example.com", IntervalSeconds = interval, TimeoutSeconds = timeout }.Validate());

    [Theory]
    [InlineData("TRACE")]
    [InlineData("DELETE")]
    public void Validate_rejects_unsafe_methods(string method) =>
        Assert.NotNull(new SyntheticMonitorRequest { Name = "n", Target = "https://example.com", Method = method }.Validate());

    [Theory]
    [InlineData(200, 0, true)]
    [InlineData(302, 0, true)]
    [InlineData(404, 0, false)]
    [InlineData(500, 0, false)]
    [InlineData(204, 204, true)]
    [InlineData(200, 204, false)]
    [InlineData(401, 401, true)]
    public void StatusMatches(int status, int expected, bool up) =>
        Assert.Equal(up, SyntheticProber.StatusMatches(status, expected));

    [Fact]
    public void ExpiryDays_is_negative_once_expired()
    {
        var now = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(10, SyntheticProber.ExpiryDays(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), now));
        Assert.Equal(-1.5, SyntheticProber.ExpiryDays(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc), now));
    }

    private static SyntheticMonitor Monitor(SyntheticMonitorKind kind, string target, int timeout = 2) => new()
    {
        Id = Guid.NewGuid(), Name = "m", Kind = kind, Target = target, TimeoutSeconds = timeout,
        CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("unused");
    }

    [Fact]
    public async Task Tcp_probe_is_up_for_a_listening_port_and_down_for_a_closed_one()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var prober = new SyntheticProber(new NoHttp(), TimeProvider.System);

        var up = await prober.ProbeAsync(Monitor(SyntheticMonitorKind.Tcp, $"127.0.0.1:{port}"), CancellationToken.None);
        listener.Stop();
        var down = await prober.ProbeAsync(Monitor(SyntheticMonitorKind.Tcp, $"127.0.0.1:{port}"), CancellationToken.None);

        Assert.True(up.Up);
        Assert.Null(up.Error);
        Assert.False(down.Up);
        Assert.NotNull(down.Error);
    }

    [Fact]
    public async Task Probe_propagates_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var prober = new SyntheticProber(new NoHttp(), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            prober.ProbeAsync(Monitor(SyntheticMonitorKind.Tcp, "127.0.0.1:9"), cts.Token));
    }
}
