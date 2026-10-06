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

    [Fact]
    public async Task Dns_probe_resolves_localhost_and_checks_the_expected_answer()
    {
        var prober = new SyntheticProber(new NoHttp(), TimeProvider.System);
        var monitor = Monitor(SyntheticMonitorKind.Dns, "localhost") with { Method = "A" };

        var up = await prober.ProbeAsync(monitor with { ExpectedAnswer = "127.0.0.1" }, CancellationToken.None);
        var wrong = await prober.ProbeAsync(monitor with { ExpectedAnswer = "203.0.113.9" }, CancellationToken.None);

        Assert.True(up.Up);
        Assert.False(wrong.Up);
        Assert.Contains("does not include", wrong.Error);
    }

    [Fact]
    public async Task Udp_probe_is_up_only_when_a_matching_reply_arrives()
    {
        using var server = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var received = await server.ReceiveAsync();
                await server.SendAsync("pong"u8.ToArray(), received.RemoteEndPoint);
            }
        });
        var prober = new SyntheticProber(new NoHttp(), TimeProvider.System);
        var monitor = Monitor(SyntheticMonitorKind.Udp, $"127.0.0.1:{port}") with { RequestBody = "ping" };

        var up = await prober.ProbeAsync(monitor with { ExpectedAnswer = "pong" }, CancellationToken.None);
        var mismatch = await prober.ProbeAsync(monitor with { ExpectedAnswer = "nope" }, CancellationToken.None);

        Assert.True(up.Up);
        Assert.False(mismatch.Up);
    }

    [Fact]
    public async Task Udp_probe_with_no_reply_is_down()
    {
        using var silent = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
        var prober = new SyntheticProber(new NoHttp(), TimeProvider.System);

        var result = await prober.ProbeAsync(Monitor(SyntheticMonitorKind.Udp, $"127.0.0.1:{port}", timeout: 1), CancellationToken.None);

        Assert.False(result.Up);
        Assert.Contains("timed out", result.Error);
    }

    [Theory]
    [InlineData(SyntheticMonitorKind.Dns, "example.com", true)]
    [InlineData(SyntheticMonitorKind.Icmp, "10.0.0.1", true)]
    [InlineData(SyntheticMonitorKind.Icmp, "::1", true)]
    [InlineData(SyntheticMonitorKind.Dns, "example.com:53", false)]
    [InlineData(SyntheticMonitorKind.Icmp, "https://example.com", false)]
    [InlineData(SyntheticMonitorKind.Udp, "example.com", false)]
    [InlineData(SyntheticMonitorKind.Udp, "example.com:53", true)]
    public void New_kinds_validate_their_targets(SyntheticMonitorKind kind, string target, bool valid) =>
        Assert.Equal(valid, new SyntheticMonitorRequest { Name = "n", Kind = kind, Target = target }.Validate() is null);

    [Fact]
    public async Task Dns_client_decodes_mx_txt_and_cname_answers_from_a_name_server()
    {
        using var server = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)server.Client.LocalEndPoint!;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var request = await server.ReceiveAsync();
                var q = request.Buffer;
                var type = (ushort)((q[^4] << 8) | q[^3]);
                byte[] rdata = type switch
                {
                    15 => [0, 10, 4, (byte)'m', (byte)'a', (byte)'i', (byte)'l', 0xC0, 12], // 10 mail.<question name>
                    16 => [3, (byte)'a', (byte)'b', (byte)'c', 2, (byte)'d', (byte)'e'],
                    _ => [3, (byte)'w', (byte)'w', (byte)'w', 0xC0, 12],
                };
                var reply = new List<byte> { q[0], q[1], 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 };
                reply.AddRange(q.Skip(12));
                reply.AddRange(new byte[] { 0xC0, 12, (byte)(type >> 8), (byte)type, 0, 1, 0, 0, 0, 60, 0, (byte)rdata.Length });
                reply.AddRange(rdata);
                await server.SendAsync(reply.ToArray(), request.RemoteEndPoint);
            }
        });

        var mx = await SyntheticDnsWire.ResolveAsync(endpoint, "example.com", SyntheticDnsWire.TypeMx, CancellationToken.None);
        var txt = await SyntheticDnsWire.ResolveAsync(endpoint, "example.com", SyntheticDnsWire.TypeTxt, CancellationToken.None);
        var cname = await SyntheticDnsWire.ResolveAsync(endpoint, "example.com", SyntheticDnsWire.TypeCname, CancellationToken.None);

        Assert.Equal(["10 mail.example.com"], mx);
        Assert.Equal(["abcde"], txt);
        Assert.Equal(["www.example.com"], cname);
    }

    [Fact]
    public void Dns_validation_checks_record_type_and_expected_address()
    {
        var ok = new SyntheticMonitorRequest { Name = "n", Kind = SyntheticMonitorKind.Dns, Target = "example.com", Method = "AAAA", ExpectedAnswer = "::1" };
        Assert.Null(ok.Validate());
        Assert.NotNull((ok with { Method = "SRV" }).Validate());
        Assert.Null((ok with { Method = "MX", ExpectedAnswer = "mail.example.com" }).Validate());
        Assert.NotNull((ok with { ExpectedAnswer = "not-an-ip" }).Validate());
    }

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

    private static readonly DateTimeOffset T1 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Status_takes_other_metrics_from_the_same_probe()
    {
        var status = SyntheticMonitorStatus.FromPoints(
        [
            new(SyntheticMetrics.Up, T1, 1),
            new(SyntheticMetrics.Duration, T1, 42.5),
            new(SyntheticMetrics.HttpStatusCode, T1, 200),
        ]);

        Assert.NotNull(status);
        Assert.True(status.Up);
        Assert.Equal(42.5, status.DurationMs);
        Assert.Equal(200, status.HttpStatus);
        Assert.Null(status.CertExpiryDays);
    }

    [Fact]
    public void Status_ignores_values_from_an_older_probe()
    {
        var status = SyntheticMonitorStatus.FromPoints(
        [
            new(SyntheticMetrics.Up, T1, 0),
            new(SyntheticMetrics.Duration, T1, 10_000),
            new(SyntheticMetrics.CertExpiryDays, T1.AddMinutes(-1), 30),
        ]);

        Assert.NotNull(status);
        Assert.False(status.Up);
        Assert.Null(status.CertExpiryDays);
    }

    [Fact]
    public void Status_is_null_without_an_up_point() =>
        Assert.Null(SyntheticMonitorStatus.FromPoints([new(SyntheticMetrics.Duration, T1, 5)]));

    [Fact]
    public void Headers_mask_every_value() =>
        Assert.Equal("Authorization: ********\nX-Env: ********", SyntheticHeaders.MaskValues("Authorization: Bearer s3cret\r\n\nX-Env: prod"));

    [Fact]
    public void Headers_restore_masked_values_by_name_and_keep_new_ones()
    {
        var restored = SyntheticHeaders.RestoreMasked(
            "authorization: ********\nX-New: fresh\nX-Env: ********",
            "Authorization: Bearer s3cret\nX-Env: prod");
        Assert.Equal("authorization: Bearer s3cret\nX-New: fresh\nX-Env: prod", restored);
    }

    [Fact]
    public void Headers_parse_name_value_lines()
    {
        var result = SyntheticHeaders.Parse("Authorization: Bearer a:b\r\n\nX-Env:  prod ");

        Assert.Null(result.Error);
        Assert.Equal([("Authorization", "Bearer a:b"), ("X-Env", "prod")], result.Headers);
    }

    [Theory]
    [InlineData("no colon here")]
    [InlineData(": value")]
    [InlineData("Bad Name: v")]
    [InlineData("X: a\u0001b")]
    public void Headers_reject_malformed_lines(string text) => Assert.NotNull(SyntheticHeaders.Parse(text).Error);

    [Fact]
    public void Request_body_requires_post()
    {
        var get = new SyntheticMonitorRequest { Name = "n", Target = "https://example.com", RequestBody = "{}" };
        Assert.NotNull(get.Validate());
        Assert.Null((get with { Method = "post" }).Validate());
    }

    [Theory]
    [InlineData("{\"ok\":true}", "ok", "", null)]
    [InlineData("{\"ok\":true}", "healthy", "", "response body does not contain the expected text")]
    [InlineData("an error page", "", "error", "response body contains the forbidden text")]
    [InlineData("fine", "fin", "error", null)]
    public void Body_assertions(string body, string contains, string notContains, string? expected) =>
        Assert.Equal(expected, SyntheticProber.BodyAssertionError(body, contains, notContains));

    private sealed class FakeHttp(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class CapturingHandler(string body, Action<HttpRequestMessage, string> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task Http_probe_sends_headers_and_body_and_checks_the_response_body()
    {
        HttpRequestMessage? seen = null;
        var sentBody = "";
        var prober = new SyntheticProber(new FakeHttp(new CapturingHandler("status: healthy", (r, b) => { seen = r; sentBody = b; })), TimeProvider.System);
        var monitor = Monitor(SyntheticMonitorKind.Http, "https://example.test/h") with
        {
            Method = "POST",
            RequestHeaders = "X-Token: abc\nContent-Type: application/json",
            RequestBody = "{\"a\":1}",
            BodyContains = "healthy",
        };

        var up = await prober.ProbeAsync(monitor, CancellationToken.None);
        var down = await prober.ProbeAsync(monitor with { BodyContains = "degraded" }, CancellationToken.None);

        Assert.True(up.Up);
        Assert.False(down.Up);
        Assert.Equal(200, down.HttpStatus);
        Assert.Equal("abc", Assert.Single(seen!.Headers.GetValues("X-Token")));
        Assert.Equal("application/json", seen.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"a\":1}", sentBody);
    }

    [Fact]
    public void Monitor_without_locations_runs_everywhere_and_with_locations_only_there()
    {
        var any = Monitor(SyntheticMonitorKind.Tcp, "db:5432");
        var scoped = any with { Locations = ["eu-west", "us-east"] };

        Assert.True(any.RunsAt("anywhere"));
        Assert.True(scoped.RunsAt("eu-west"));
        Assert.False(scoped.RunsAt("ap-south"));
        Assert.False(scoped.RunsAt("EU-WEST"));
    }

    [Fact]
    public void Request_normalizes_locations()
    {
        var request = new SyntheticMonitorRequest
        {
            Name = "m",
            Target = "https://example.test",
            Locations = [" eu-west ", "eu-west", "", "us.east_1"],
        };

        Assert.Equal(["eu-west", "us.east_1"], request.NormalizedLocations());
        Assert.Null(request.Validate());
    }

    [Theory]
    [InlineData("eu west")]
    [InlineData("eu/west")]
    [InlineData("é")]
    public void Request_rejects_invalid_location_names(string location) =>
        Assert.NotNull(new SyntheticMonitorRequest { Name = "m", Target = "https://example.test", Locations = [location] }.Validate());

    [Fact]
    public void Request_rejects_too_many_or_too_long_locations()
    {
        var many = Enumerable.Range(0, SyntheticMonitorRequest.MaxLocations + 1).Select(i => $"l{i}").ToList();
        Assert.NotNull(new SyntheticMonitorRequest { Name = "m", Target = "https://example.test", Locations = many }.Validate());
        Assert.NotNull(new SyntheticMonitorRequest { Name = "m", Target = "https://example.test", Locations = [new string('a', 65)] }.Validate());
    }
}
