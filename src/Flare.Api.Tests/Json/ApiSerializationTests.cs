using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Flare.Api.Json;
using Flare.Api.Model;
using MemoryPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Json;

/// <summary>
/// Exercises <see cref="ApiSerialization"/>'s JSON/MemoryPack content negotiation
/// directly - Phase 1 of
/// docs-internal/investigations/memorypack-serialization-migration-scope.md. Pure/no-
/// infra, same "fake the one seam, execute the real IResult" convention as the
/// Endpoints tests (e.g. <c>UserEndpointsTests</c>).
/// </summary>
public class ApiSerializationTests
{
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext CreateContext(string? accept = null, string? contentType = null, byte[]? requestBody = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }

        if (contentType is not null)
        {
            context.Request.ContentType = contentType;
        }

        if (requestBody is not null)
        {
            context.Request.Body = new MemoryStream(requestBody);
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("application/json", false)]
    [InlineData("text/html, application/json;q=0.9", false)]
    [InlineData("application/x-memorypack", true)]
    [InlineData("application/json;q=0.5, application/x-memorypack", true)]
    public void WantsMemoryPack_ReflectsAcceptHeader(string? accept, bool expected)
    {
        var context = CreateContext(accept: accept);
        Assert.Equal(expected, ApiSerialization.WantsMemoryPack(context.Request));
    }

    [Fact]
    public async Task Write_DefaultAccept_WritesJson_UnchangedFromResultsJson()
    {
        var context = CreateContext();
        var response = new AlertRuleListResponse { Rules = [] };

        var result = ApiSerialization.Write(context, response, AlertsJsonContext.Default.AlertRuleListResponse);
        await result.ExecuteAsync(context);

        Assert.StartsWith("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        var dto = await JsonSerializer.DeserializeAsync(context.Response.Body, AlertsJsonContext.Default.AlertRuleListResponse, TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.Empty(dto!.Rules);
    }

    [Fact]
    public async Task Write_MemoryPackAccept_WritesMemoryPackBytes_RoundTrips()
    {
        var context = CreateContext(accept: ApiSerialization.MemoryPackContentType);
        var response = new AlertRuleListResponse { Rules = [] };

        var result = ApiSerialization.Write(context, response, AlertsJsonContext.Default.AlertRuleListResponse);
        await result.ExecuteAsync(context);

        Assert.Equal(ApiSerialization.MemoryPackContentType, context.Response.ContentType);
        context.Response.Body.Position = 0;
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        var dto = MemoryPackSerializer.Deserialize<AlertRuleListResponse>(bytes);
        Assert.NotNull(dto);
        Assert.Empty(dto!.Rules);
    }

    [Fact]
    public async Task Write_MemoryPackAccept_SetsRequestedStatusCode()
    {
        var context = CreateContext(accept: ApiSerialization.MemoryPackContentType);
        var response = new AlertRuleListResponse { Rules = [] };

        var result = ApiSerialization.Write(context, response, AlertsJsonContext.Default.AlertRuleListResponse, statusCode: StatusCodes.Status201Created);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
    }

    /// <summary>
    /// The MemoryPack body is streamed rather than built as one array up front, so a
    /// serializer failure must still happen before any byte reaches the response, leaving it
    /// unstarted so exception handling can turn it into a 500.
    /// </summary>
    [Fact]
    public async Task Write_MemoryPackAccept_SerializerFailure_WritesNothing()
    {
        var context = CreateContext(accept: ApiSerialization.MemoryPackContentType);
        var jsonTypeInfo = (JsonTypeInfo<NotMemoryPackable>)JsonSerializerOptions.Default.GetTypeInfo(typeof(NotMemoryPackable));

        var result = ApiSerialization.Write(context, new NotMemoryPackable(), jsonTypeInfo);

        await Assert.ThrowsAsync<MemoryPackSerializationException>(() => result.ExecuteAsync(context));
        Assert.Equal(0, context.Response.Body.Length);
        Assert.False(context.Response.HasStarted);
    }

    [Fact]
    public async Task Write_MemoryPackAccept_LargeBody_RoundTrips()
    {
        var context = CreateContext(accept: ApiSerialization.MemoryPackContentType);
        // Well past the 85 kB LOH threshold and past one pooled buffer segment.
        var response = new LoginRequest { Username = "alice", Password = string.Concat(Enumerable.Range(0, 30_000).Select(i => i.ToString("D6"))) };

        var result = ApiSerialization.Write(context, response, AuthJsonContext.Default.LoginRequest);
        await result.ExecuteAsync(context);

        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        Assert.True(bytes.Length > 150_000);
        Assert.Equal(MemoryPackSerializer.Serialize(response), bytes);
    }

    private sealed class NotMemoryPackable
    {
        public int Value { get; set; }
    }

    [Fact]
    public async Task ReadAsync_DefaultContentType_ReadsJson()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new LoginRequest { Username = "alice", Password = "hunter2" }, AuthJsonContext.Default.LoginRequest);
        var context = CreateContext(requestBody: body);

        var request = await ApiSerialization.ReadAsync(context, AuthJsonContext.Default.LoginRequest, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("alice", request!.Username);
        Assert.Equal("hunter2", request.Password);
    }

    [Fact]
    public async Task ReadAsync_MemoryPackContentType_ReadsMemoryPack_RoundTrips()
    {
        var body = MemoryPackSerializer.Serialize(new LoginRequest { Username = "bob", Password = "correcthorse" });
        var context = CreateContext(contentType: ApiSerialization.MemoryPackContentType, requestBody: body);

        var request = await ApiSerialization.ReadAsync(context, AuthJsonContext.Default.LoginRequest, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("bob", request!.Username);
        Assert.Equal("correcthorse", request.Password);
    }

    /// <summary>
    /// Regression test for a bug a live end-to-end check found (2026-09-01, see the
    /// investigation doc's Phase 1 follow-ups): a malformed MemoryPack body used to throw
    /// <c>MemoryPackSerializationException</c>, which the existing per-endpoint
    /// <c>catch (JsonException ex)</c> blocks didn't catch - an unhandled 500 where a
    /// malformed JSON body already got a clean 400. <see cref="ApiSerialization.ReadAsync{T}"/>
    /// now rewraps it as <see cref="JsonException"/> so those catch sites keep working
    /// unmodified.
    /// </summary>
    [Fact]
    public async Task ReadAsync_MalformedMemoryPackBody_ThrowsJsonException()
    {
        var context = CreateContext(contentType: ApiSerialization.MemoryPackContentType, requestBody: "not memorypack bytes at all"u8.ToArray());

        await Assert.ThrowsAsync<JsonException>(() =>
            ApiSerialization.ReadAsync(context, AuthJsonContext.Default.LoginRequest, CancellationToken.None).AsTask());
    }

    /// <summary>
    /// The one DTO that needed a hand-written formatter (Phase 0 - see
    /// <see cref="JsonElementMemoryPackFormatter"/>'s remarks): confirms
    /// <c>SavedView.State</c>'s opaque <see cref="JsonElement"/> survives the full
    /// negotiated Write path, not just a direct <c>MemoryPackSerializer</c> call.
    /// </summary>
    [Fact]
    public async Task Write_MemoryPackAccept_RoundTripsOpaqueJsonElementState()
    {
        var context = CreateContext(accept: ApiSerialization.MemoryPackContentType);
        var view = new SavedView
        {
            Id = Guid.NewGuid(),
            Name = "My view",
            PageType = SavedViewPageType.Logs,
            State = JsonDocument.Parse("""{"service":"flare-api","severity":["Error"]}""").RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var result = ApiSerialization.Write(context, view, SavedViewsJsonContext.Default.SavedView);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        var dto = MemoryPackSerializer.Deserialize<SavedView>(bytes);

        Assert.NotNull(dto);
        Assert.Equal("flare-api", dto!.State.GetProperty("service").GetString());
        Assert.Equal("Error", dto.State.GetProperty("severity")[0].GetString());
    }
}
