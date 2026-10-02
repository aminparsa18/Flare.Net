using System.Net;
using System.Text;
using Flare.Cli.Commands;
using ModelContextProtocol;
using Xunit;

namespace Flare.Cli.Tests;

/// <summary>Drives the MCP tools against a stubbed HTTP handler - no network, no stack.</summary>
public class FlareMcpToolsTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FlareMcpTools Tools(StubHandler handler, bool initialized = true) =>
        new(new FlareApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://flare.test") }, initialized));

    [Fact]
    public async Task NotInitialized_ThrowsMcpExceptionWithoutCallingApi()
    {
        var handler = new StubHandler(_ => Json("{}"));

        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(handler, initialized: false).SearchLogs());

        Assert.Contains("flare start", ex.Message);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Unauthorized_SaysHowToPassAToken()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(handler).SearchLogs());

        Assert.Contains("401", ex.Message);
        Assert.Contains("FLARE_API_TOKEN", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_OnRunLookup_IsAnErrorNotAnEmptyResult()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(handler).ListRuns("api"));

        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task SearchLogs_RejectsUnknownLevelBeforeCallingApi()
    {
        var handler = new StubHandler(_ => Json("{}"));

        await Assert.ThrowsAsync<McpException>(() => Tools(handler).SearchLogs(levels: ["loud"]));

        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task SearchLogs_LastRunWithoutServices_IsRejected()
    {
        var handler = new StubHandler(_ => Json("{}"));

        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(handler).SearchLogs(lastRun: true));

        Assert.Contains("services", ex.Message);
    }

    [Fact]
    public async Task SearchLogs_CapsPageSizeAtOneHundredAndTruncatesBodies()
    {
        string? sentBody = null;
        var handler = new StubHandler(req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            var longBody = new string('x', 1000);
            return Json($$"""{"events":[{"eventId":"{{Guid.NewGuid()}}","timestamp":"2026-10-02T10:00:00Z","severityText":"INFO","severityNumber":9,"serviceName":"api","body":"{{longBody}}"}]}""");
        });

        var text = await Tools(handler).SearchLogs(limit: 5000);

        Assert.Contains("\"pageSize\":100", sentBody);
        Assert.True(text.Length < 500, "a 1000-char body must be truncated");
        Assert.EndsWith("…" + Environment.NewLine, text);
    }

    [Fact]
    public async Task CompareRuns_IgnoresRootsOfAnotherOperation_WhenServerIgnoresNamesFilter()
    {
        // An API older than the Names filter returns the newest roots regardless of name.
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/spans/attribute-values" => Json("""{"values":[{"value":"a","count":1},{"value":"b","count":1}]}"""),
            "/api/spans/search" when req.Content!.ReadAsStringAsync().Result.Contains("service.instance.id") =>
                Json(req.Content!.ReadAsStringAsync().Result.Contains("\"a\"")
                    ? """{"spans":[{"traceId":"t1","serviceName":"api","name":"x","statusCode":"OK","startTime":"2026-10-02T10:00:00Z"}]}"""
                    : """{"spans":[{"traceId":"t2","serviceName":"api","name":"x","statusCode":"OK","startTime":"2026-10-02T10:30:00Z"}]}"""),
            "/api/spans/search" => Json("""{"spans":[{"traceId":"other","serviceName":"api","name":"GET /other","statusCode":"OK","startTime":"2026-10-02T10:31:00Z"}]}"""),
            _ => Json("{}", HttpStatusCode.NotFound),
        });

        var text = await Tools(handler).CompareRuns("api", "POST /checkout");

        Assert.Contains("No 'POST /checkout' trace", text);
        Assert.DoesNotContain("trace=other", text);
    }
}
