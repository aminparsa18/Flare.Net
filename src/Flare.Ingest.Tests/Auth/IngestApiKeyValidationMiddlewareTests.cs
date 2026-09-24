using Flare.Identity.IngestKeys;
using Flare.Ingest.Auth;
using Flare.Ingest.Stats;
using Flare.Ingest.Tests.Auth.TestSupport;
using Google.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Ingest.Tests.Auth;

public class IngestApiKeyValidationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_CallsNext_WhenKeyIsNotRequired_RegardlessOfHeader()
    {
        var middleware = (await CreateAsync(new IngestAuthOptions { IngestKeyRequired = false })).Middleware;
        var context = CreateHttpContext(path: "/v1/logs");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenRequired_AndNoAuthorizationHeaderIsPresent()
    {
        var middleware = (await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" })).Middleware;
        var context = CreateHttpContext(path: "/v1/logs");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenRequired_AndTheKeyIsWrong()
    {
        var middleware = (await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" })).Middleware;
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: "wrong-key");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenRequired_AndTheKeyIsValid()
    {
        var middleware = (await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" })).Middleware;
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: "valid-key");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_ForHealthCheckPaths_EvenWhenRequired_AndNoKeyIsPresent()
    {
        var middleware = (await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" })).Middleware;
        var context = CreateHttpContext(path: "/health");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ValidatesGrpcRequests_ByContentTypeRatherThanPath()
    {
        var middleware = (await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" })).Middleware;
        // gRPC paths are proto-service-qualified (e.g.
        // /opentelemetry.proto.collector.logs.v1.LogsService/Export), not /v1/* -
        // IsOtlpRequest has to key off content-type for these, not the path.
        var context = CreateHttpContext(path: "/opentelemetry.proto.collector.logs.v1.LogsService/Export");
        context.Request.ContentType = "application/grpc";

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateHttpContext(string path, string? bearerToken = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (bearerToken is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        }
        return context;
    }

    [Fact]
    public async Task InvokeAsync_RecordsAcceptedUsage_ForASqliteKey_WithoutReadingUsage_WhenNoLimitsAreSet()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (keyId, rawKey) = await fixture.AddKeyAsync("prod-collector", IngestApiKeyLimits.None);
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: rawKey);

        await fixture.Middleware.InvokeAsync(context, ctx => { IngestKeyUsageFeature.Add(ctx, 7, 1_024); return Task.CompletedTask; });

        Assert.Equal(0, fixture.Usage.GetCalls);
        Assert.Equal([(keyId, 7L, 1_024L)], fixture.Usage.Recorded);
    }

    [Fact]
    public async Task InvokeAsync_RecordsNothing_WhenTheReceiverAcceptedNothing()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (_, rawKey) = await fixture.AddKeyAsync("prod-collector", IngestApiKeyLimits.None);
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: rawKey);

        await fixture.Middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Empty(fixture.Usage.Recorded);
    }

    [Fact]
    public async Task InvokeAsync_NeitherChecksNorRecordsUsage_ForTheStaticKey()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" });
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: "valid-key");

        var nextCalled = false;
        await fixture.Middleware.InvokeAsync(context, ctx => { nextCalled = true; IngestKeyUsageFeature.Add(ctx, 7, 1_024); return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Null(context.Features.Get<IngestKeyUsageFeature>());
        Assert.Empty(fixture.Usage.Recorded);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenUnderEveryEnforcedCap()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (keyId, rawKey) = await fixture.AddKeyAsync("limited", new IngestApiKeyLimits(true, MaxEventsPerMinute: 100, null, null, null));
        fixture.Usage.Usage[keyId] = new IngestKeyUsage(new IngestKeyWindowUsage(99, 0), new IngestKeyWindowUsage(99, 0));
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: rawKey);

        var nextCalled = false;
        await fixture.Middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Equal(1, fixture.Usage.GetCalls);
    }

    [Fact]
    public async Task InvokeAsync_Returns429WithRetryInfoStatusBody_OverHttp_WhenACapIsReached()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (keyId, rawKey) = await fixture.AddKeyAsync("limited", new IngestApiKeyLimits(true, MaxEventsPerMinute: 100, null, null, null));
        fixture.Usage.Usage[keyId] = new IngestKeyUsage(new IngestKeyWindowUsage(100, 0), new IngestKeyWindowUsage(100, 0));
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: rawKey);
        context.Request.ContentType = "application/x-protobuf";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        await fixture.Middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        var retryAfter = int.Parse(context.Response.Headers.RetryAfter.ToString());
        Assert.InRange(retryAfter, 1, 60);

        context.Response.Body.Position = 0;
        var status = Google.Rpc.Status.Parser.ParseFrom(context.Response.Body);
        Assert.Equal((int)Grpc.Core.StatusCode.ResourceExhausted, status.Code);
        Assert.Equal(retryAfter, status.Details.Single().Unpack<RetryInfo>().RetryDelay.Seconds);

        Assert.Equal([(IngestionSignal.Logs, IngestionProtocol.Http, "ingest-key-limit:limited")], fixture.Stats.Rejected);
        Assert.Empty(fixture.Usage.Recorded);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsTrailersOnlyResourceExhausted_OverGrpc_WhenACapIsReached()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (keyId, rawKey) = await fixture.AddKeyAsync("limited", new IngestApiKeyLimits(true, null, null, null, MaxBytesPerDay: 1_000));
        fixture.Usage.Usage[keyId] = new IngestKeyUsage(new IngestKeyWindowUsage(0, 0), new IngestKeyWindowUsage(0, 1_000));
        var context = CreateHttpContext(path: "/opentelemetry.proto.collector.trace.v1.TraceService/Export", bearerToken: rawKey);
        context.Request.ContentType = "application/grpc";

        var nextCalled = false;
        await fixture.Middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("8", context.Response.Headers["grpc-status"].ToString());
        var status = Google.Rpc.Status.Parser.ParseFrom(Convert.FromBase64String(context.Response.Headers["grpc-status-details-bin"].ToString()));
        Assert.True(status.Details.Single().Unpack<RetryInfo>().RetryDelay.Seconds > 0);
        Assert.Equal([(IngestionSignal.Traces, IngestionProtocol.Grpc, "ingest-key-limit:limited")], fixture.Stats.Rejected);
    }

    [Fact]
    public async Task InvokeAsync_WritesAJsonStatusBody_ForAJsonRequest()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (keyId, rawKey) = await fixture.AddKeyAsync("limited", new IngestApiKeyLimits(true, MaxEventsPerMinute: 1, null, null, null));
        fixture.Usage.Usage[keyId] = new IngestKeyUsage(new IngestKeyWindowUsage(1, 0), default);
        var context = CreateHttpContext(path: "/v1/metrics", bearerToken: rawKey);
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        await fixture.Middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("google.rpc.RetryInfo", json);
    }

    [Fact]
    public async Task InvokeAsync_IgnoresConfiguredCaps_WhenLimitsAreDisabled()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (keyId, rawKey) = await fixture.AddKeyAsync("toggled-off", new IngestApiKeyLimits(false, MaxEventsPerMinute: 1, null, null, null));
        fixture.Usage.Usage[keyId] = new IngestKeyUsage(new IngestKeyWindowUsage(1_000, 0), default);
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: rawKey);

        var nextCalled = false;
        await fixture.Middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Equal(0, fixture.Usage.GetCalls);
    }

    [Fact]
    public async Task InvokeAsync_FailsOpen_WhenTheUsageReadThrows()
    {
        var fixture = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true });
        var (_, rawKey) = await fixture.AddKeyAsync("limited", new IngestApiKeyLimits(true, MaxEventsPerMinute: 1, null, null, null));
        fixture.Usage.ThrowOnGet = true;
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: rawKey);

        var nextCalled = false;
        await fixture.Middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    private static async Task<Fixture> CreateAsync(IngestAuthOptions options)
    {
        var fixture = new Fixture(options, new FakeIngestApiKeyStore(), new FakeIngestKeyUsageStore(), new FakeIngestionStatsTracker());
        await fixture.Cache.InitializeAsync(CancellationToken.None);
        return fixture;
    }

    private sealed class Fixture(IngestAuthOptions options, FakeIngestApiKeyStore store, FakeIngestKeyUsageStore usage, FakeIngestionStatsTracker stats)
    {
        public IngestApiKeyCache Cache { get; } = new(store, Options.Create(options), NullLogger<IngestApiKeyCache>.Instance);

        public FakeIngestKeyUsageStore Usage => usage;

        public FakeIngestionStatsTracker Stats => stats;

        public TestMiddleware Middleware => new(Options.Create(options), Cache, usage, stats);

        /// <summary>Creates a SQLite-style key with <paramref name="limits"/> and refreshes
        /// the cache so the middleware sees it.</summary>
        public async Task<(Guid KeyId, string RawKey)> AddKeyAsync(string name, IngestApiKeyLimits limits)
        {
            var (key, rawKey) = await store.CreateAsync(name);
            await store.UpdateLimitsAsync(key.Id, limits);
            await Cache.InitializeAsync(CancellationToken.None);
            return (key.Id, rawKey);
        }
    }

    /// <summary>
    /// IngestApiKeyValidationMiddleware's real constructor takes a <see cref="RequestDelegate"/>
    /// bound at pipeline-build time - this thin subclass exposes an InvokeAsync overload
    /// that takes the "next" delegate per-call instead, which is all a unit test needs
    /// (no real middleware pipeline/host involved).
    /// </summary>
    private sealed class TestMiddleware(IOptions<IngestAuthOptions> options, IngestApiKeyCache cache, IIngestKeyUsageStore usage, IIngestionStatsTracker stats)
    {
        public Task InvokeAsync(HttpContext context, RequestDelegate next) =>
            new IngestApiKeyValidationMiddleware(next, options, cache, usage, stats, TimeProvider.System, NullLogger<IngestApiKeyValidationMiddleware>.Instance).InvokeAsync(context);
    }
}
