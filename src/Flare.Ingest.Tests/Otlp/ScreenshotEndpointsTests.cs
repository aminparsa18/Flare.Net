using Flare.Ingest.Auth;
using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Ingest.Tests.Otlp;

public class ScreenshotEndpointsTests
{
    private sealed class FakeWriter : IClickHouseScreenshotWriter
    {
        public List<ScreenshotRecord> Written { get; } = [];
        public Task WriteAsync(ScreenshotRecord s, DateTimeOffset at, CancellationToken ct = default)
        {
            Written.Add(s);
            return Task.CompletedTask;
        }
    }

    private const string Trace = "0123456789abcdef0123456789abcdef";
    private const string Span = "0123456789abcdef";

    private static async Task<(int Status, FakeWriter Writer)> PostAsync(
        byte[] body, string contentType = "image/jpeg", string service = "shop", string? query = null, params string[] allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(body);
        context.Request.QueryString = new QueryString(query ?? $"?service={service}&session_id=abcdef0123456789&trace_id={Trace}&span_id={Span}");
        if (allowed.Length > 0)
            context.Features.Set(new IngestKeyUsageFeature(Guid.NewGuid()) { AllowedServices = allowed.ToHashSet() });

        var writer = new FakeWriter();
        var result = await ScreenshotEndpoints.HandleAsync(context, writer, TimeProvider.System, NullLoggerFactory.Instance, CancellationToken.None);
        var status = result switch
        {
            NoContent => 204,
            IStatusCodeHttpResult s => s.StatusCode ?? 200,
            _ => 0,
        };
        return (status, writer);
    }

    [Fact]
    public async Task Valid_screenshot_is_stored()
    {
        var (status, writer) = await PostAsync([1, 2, 3]);

        Assert.Equal(204, status);
        var row = Assert.Single(writer.Written);
        Assert.Equal(("shop", Trace, Span, "image/jpeg"), (row.ServiceName, row.TraceId, row.SpanId, row.ContentType));
        Assert.Equal(new byte[] { 1, 2, 3 }, row.Image);
    }

    [Theory]
    [InlineData("text/plain", 415)]
    [InlineData("image/jpeg; charset=binary", 204)]
    public async Task Content_type_must_be_an_image(string contentType, int expected) =>
        Assert.Equal(expected, (await PostAsync([1], contentType)).Status);

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var (status, writer) = await PostAsync(new byte[ScreenshotEndpoints.MaxImageBytes + 1]);
        Assert.Equal(413, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Missing_or_malformed_ids_are_a_bad_request()
    {
        Assert.Equal(400, (await PostAsync([1], query: "?service=shop&session_id=abcdef0123456789&trace_id=nothex&span_id=" + Span)).Status);
        Assert.Equal(400, (await PostAsync([1], query: "?session_id=abcdef0123456789&trace_id=" + Trace + "&span_id=" + Span)).Status);
    }

    [Fact]
    public async Task Service_outside_the_keys_allowlist_is_forbidden()
    {
        var (status, writer) = await PostAsync([1], allowed: "other");
        Assert.Equal(403, status);
        Assert.Empty(writer.Written);
    }
}
