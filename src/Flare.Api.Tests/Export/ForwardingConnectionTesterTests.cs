using System.Net;
using Flare.Api.Export;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Export;

public class ForwardingConnectionTesterTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }
        public long BodyLength { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            BodyLength = request.Content is null ? -1 : (await request.Content.ReadAsByteArrayAsync(cancellationToken)).Length;
            return await respond(request);
        }
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode code) => Task.FromResult(new HttpResponseMessage(code));

    [Theory]
    [InlineData("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp", "https://otlp-gateway-prod-eu-west-2.grafana.net/otlp/v1/logs")]
    [InlineData(" http://collector:4318/ ", "http://collector:4318/v1/logs")]
    public void ProbeUrl_appends_the_logs_path(string endpoint, string expected) =>
        Assert.Equal(expected, ForwardingConnectionTester.ProbeUrl(endpoint));

    [Fact]
    public async Task Success_posts_an_empty_protobuf_body_with_the_headers()
    {
        var handler = new StubHandler(_ => Reply(HttpStatusCode.OK));
        var result = await ForwardingConnectionTester.TestAsync(new HttpClient(handler), "http://collector:4318",
            new Dictionary<string, string> { ["Authorization"] = "Basic abc" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("", result.Error);
        Assert.Equal(HttpMethod.Post, handler.Seen!.Method);
        Assert.Equal("http://collector:4318/v1/logs", handler.Seen.RequestUri!.ToString());
        Assert.Equal("Basic abc", handler.Seen.Headers.Authorization!.ToString());
        Assert.Equal("application/x-protobuf", handler.Seen.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(0, handler.BodyLength);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the credentials")]
    [InlineData(HttpStatusCode.Forbidden, "rejected the credentials")]
    [InlineData(HttpStatusCode.NotFound, "no OTLP/HTTP receiver")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limiting")]
    [InlineData(HttpStatusCode.Found, "redirected")]
    [InlineData(HttpStatusCode.BadGateway, "HTTP 502")]
    public async Task Non_success_status_is_reported_with_a_hint(HttpStatusCode code, string expected)
    {
        var result = await ForwardingConnectionTester.TestAsync(new HttpClient(new StubHandler(_ => Reply(code))), "http://x", new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal((int)code, result.StatusCode);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task Connection_failure_reports_status_zero()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Name or service not known"));
        var result = await ForwardingConnectionTester.TestAsync(new HttpClient(handler), "http://nope.invalid", new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.Equal("Name or service not known", result.Error);
    }

    [Fact]
    public async Task Timeout_is_reported_not_thrown()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));
        var result = await ForwardingConnectionTester.TestAsync(new HttpClient(handler), "http://slow", new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("did not answer", result.Error);
    }

    [Fact]
    public async Task Caller_cancellation_still_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHandler(_ => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ForwardingConnectionTester.TestAsync(new HttpClient(handler), "http://x", new Dictionary<string, string>(), cts.Token));
    }

    [Theory]
    [InlineData("https://collector:4318", true)]
    [InlineData("collector:4318", false)]
    [InlineData("ftp://collector", false)]
    [InlineData("", false)]
    public void Request_validation_requires_an_http_url(string endpoint, bool valid) =>
        Assert.Equal(valid, new ForwardingTestRequest { Endpoint = endpoint }.Validate() is null);
}
