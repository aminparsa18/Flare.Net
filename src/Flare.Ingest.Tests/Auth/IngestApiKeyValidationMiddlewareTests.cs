using Flare.Ingest.Auth;
using Flare.Ingest.Tests.Auth.TestSupport;
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
        var (middleware, _) = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = false });
        var context = CreateHttpContext(path: "/v1/logs");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenRequired_AndNoAuthorizationHeaderIsPresent()
    {
        var (middleware, _) = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" });
        var context = CreateHttpContext(path: "/v1/logs");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenRequired_AndTheKeyIsWrong()
    {
        var (middleware, _) = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" });
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: "wrong-key");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenRequired_AndTheKeyIsValid()
    {
        var (middleware, _) = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" });
        var context = CreateHttpContext(path: "/v1/logs", bearerToken: "valid-key");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_CallsNext_ForHealthCheckPaths_EvenWhenRequired_AndNoKeyIsPresent()
    {
        var (middleware, _) = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" });
        var context = CreateHttpContext(path: "/health");

        var nextCalled = false;
        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ValidatesGrpcRequests_ByContentTypeRatherThanPath()
    {
        var (middleware, _) = await CreateAsync(new IngestAuthOptions { IngestKeyRequired = true, StaticIngestApiKey = "valid-key" });
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

    private static async Task<(TestMiddleware Middleware, IngestApiKeyCache Cache)> CreateAsync(IngestAuthOptions options)
    {
        var store = new FakeIngestApiKeyStore();
        var cache = new IngestApiKeyCache(store, Options.Create(options), NullLogger<IngestApiKeyCache>.Instance);
        await cache.InitializeAsync(CancellationToken.None);
        return (new TestMiddleware(Options.Create(options), cache), cache);
    }

    /// <summary>
    /// IngestApiKeyValidationMiddleware's real constructor takes a <see cref="RequestDelegate"/>
    /// bound at pipeline-build time - this thin subclass exposes an InvokeAsync overload
    /// that takes the "next" delegate per-call instead, which is all a unit test needs
    /// (no real middleware pipeline/host involved).
    /// </summary>
    private sealed class TestMiddleware(IOptions<IngestAuthOptions> options, IngestApiKeyCache cache)
    {
        public Task InvokeAsync(HttpContext context, RequestDelegate next) =>
            new IngestApiKeyValidationMiddleware(next, options, cache).InvokeAsync(context);
    }
}
