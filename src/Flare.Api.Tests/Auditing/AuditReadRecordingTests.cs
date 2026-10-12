using System.Security.Claims;
using Flare.Api.Auditing;
using Flare.Identity.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Auditing;

public class AuditReadRecordingTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Theory]
    [InlineData("POST", "/api/logs/search", "logs", "query", null)]
    [InlineData("POST", "/api/metrics/query", "metrics", "query", null)]
    [InlineData("GET", "/api/traces/{traceId}", "traces", "view", "traceId")]
    [InlineData("GET", "/api/logs/tail", "logs", "tail", null)]
    public void ClassifyRead_TelemetryRoute_ReturnsClassification(string method, string template, string type, string action, string? idKey)
    {
        var result = AuditActionClassifier.ClassifyRead(method, template);

        Assert.NotNull(result);
        Assert.Equal(type, result.ResourceType);
        Assert.Equal(action, result.Action);
        Assert.Equal(idKey, result.ResourceIdRouteValue);
    }

    [Theory]
    [InlineData("POST", "/api/logs/attribute-values")]
    [InlineData("POST", "/api/metrics/names")]
    [InlineData("PUT", "/api/alerts/{id:guid}")]
    public void ClassifyRead_MetadataOrWriteRoute_ReturnsNull(string method, string template) =>
        Assert.Null(AuditActionClassifier.ClassifyRead(method, template));

    [Fact]
    public void Classify_DoesNotTreatReadsAsStateChanges() =>
        Assert.Null(AuditActionClassifier.Classify("POST", "/api/logs/search"));

    [Fact]
    public void Throttle_RecordsOncePerWindowPerKey()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AuditReadThrottle(clock);
        var window = TimeSpan.FromSeconds(60);

        Assert.True(throttle.ShouldRecord("a", window));
        Assert.False(throttle.ShouldRecord("a", window));
        Assert.True(throttle.ShouldRecord("b", window));
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(throttle.ShouldRecord("a", window));
        Assert.True(throttle.ShouldRecord("a", TimeSpan.Zero));
        Assert.True(throttle.ShouldRecord("a", TimeSpan.Zero));
    }

    private sealed class CapturingStore : IAuditEventStore
    {
        public List<NewAuditEvent> Events { get; } = [];

        public Task AppendAsync(NewAuditEvent auditEvent, CancellationToken ct)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditEventFilter filter, long? beforeId, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEvent>> QueryAfterAsync(long afterId, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> GetForwardingCursorAsync(string sink, CancellationToken ct) => throw new NotSupportedException();
        public Task SetForwardingCursorAsync(string sink, long lastId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneAsync(DateTimeOffset cutoff, CancellationToken ct) => throw new NotSupportedException();
    }

    private static async Task Invoke(
        CapturingStore store, AuditOptions options, AuditReadThrottle throttle,
        string method, string template, int status, string? traceId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()), new Claim(ClaimTypes.Name, "alice")], "test"));
        if (traceId is not null)
        {
            context.Request.RouteValues["traceId"] = traceId;
        }

        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, "t"));
        var middleware = new AuditMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = status;
                return Task.CompletedTask;
            },
            NullLogger<AuditMiddleware>.Instance);
        await middleware.InvokeAsync(context, store, TimeProvider.System, Options.Create(options), throttle);
    }

    [Fact]
    public async Task Middleware_RecordsNothingForReads_WhenDisabled()
    {
        var store = new CapturingStore();

        await Invoke(store, new AuditOptions(), new AuditReadThrottle(TimeProvider.System), "POST", "/api/logs/search", 200);

        Assert.Empty(store.Events);
    }

    [Fact]
    public async Task Middleware_RecordsReadOnce_WhenEnabled_ThenDedupes()
    {
        var store = new CapturingStore();
        var options = new AuditOptions { RecordReads = true };
        var throttle = new AuditReadThrottle(TimeProvider.System);

        await Invoke(store, options, throttle, "POST", "/api/logs/search", 200);
        await Invoke(store, options, throttle, "POST", "/api/logs/search", 200);

        var e = Assert.Single(store.Events);
        Assert.Equal(("logs", "query", "alice", 200), (e.ResourceType, e.Action, e.ActorName, e.StatusCode));
    }

    [Fact]
    public async Task Middleware_RecordsTraceIdAsResource_AndDistinctTracesSeparately()
    {
        var store = new CapturingStore();
        var options = new AuditOptions { RecordReads = true };
        var throttle = new AuditReadThrottle(TimeProvider.System);

        await Invoke(store, options, throttle, "GET", "/api/traces/{traceId}", 200, "t1");
        await Invoke(store, options, throttle, "GET", "/api/traces/{traceId}", 200, "t2");

        Assert.Equal(["t1", "t2"], store.Events.Select(e => e.ResourceId));
    }

    [Fact]
    public async Task Middleware_IgnoresFailedReads_AndRecordsWebSocketTail()
    {
        var store = new CapturingStore();
        var options = new AuditOptions { RecordReads = true };
        var throttle = new AuditReadThrottle(TimeProvider.System);

        await Invoke(store, options, throttle, "POST", "/api/logs/search", 403);
        await Invoke(store, options, throttle, "GET", "/api/logs/tail", 101);

        var e = Assert.Single(store.Events);
        Assert.Equal(("tail", 101), (e.Action, e.StatusCode));
    }
}
