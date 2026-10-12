using System.Text;
using Flare.Ingest.Auth;
using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Ingest.Tests.Otlp;

public class FeedbackEndpointsTests
{
    private sealed class FakeWriter : IClickHouseFeedbackWriter
    {
        public List<FeedbackRecord> Written { get; } = [];
        public Task WriteAsync(FeedbackRecord f, DateTimeOffset at, CancellationToken ct = default)
        {
            Written.Add(f);
            return Task.CompletedTask;
        }
    }

    private const string Valid = """{"service":"shop","session_id":"abcdef0123456789","message":"The cart froze","email":"a@b.c","user_id":"u1","app_version":"1.2.3"}""";

    private static async Task<(int Status, FakeWriter Writer)> PostAsync(
        string body, string contentType = "application/json", params string[] allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (allowed.Length > 0)
            context.Features.Set(new IngestKeyUsageFeature(Guid.NewGuid()) { AllowedServices = allowed.ToHashSet() });

        var writer = new FakeWriter();
        var result = await FeedbackEndpoints.HandleAsync(context, writer, TimeProvider.System, NullLoggerFactory.Instance, CancellationToken.None);
        var status = result switch
        {
            NoContent => 204,
            IStatusCodeHttpResult s => s.StatusCode ?? 200,
            _ => 0,
        };
        return (status, writer);
    }

    [Fact]
    public async Task Valid_feedback_is_stored()
    {
        var (status, writer) = await PostAsync(Valid);

        Assert.Equal(204, status);
        var row = Assert.Single(writer.Written);
        Assert.Equal(("shop", "abcdef0123456789", "The cart froze", "a@b.c", "u1", "1.2.3"),
            (row.ServiceName, row.SessionId, row.Message, row.Email, row.UserId, row.AppVersion));
        Assert.NotEqual(Guid.Empty, row.FeedbackId);
    }

    [Fact]
    public async Task Optional_fields_default_to_empty()
    {
        var (_, writer) = await PostAsync("""{"service":"shop","session_id":"abcdef0123456789","message":"hi"}""");
        var row = Assert.Single(writer.Written);
        Assert.Equal(("", "", "", ""), (row.Email, row.Name, row.UserId, row.AppVersion));
    }

    [Theory]
    [InlineData("""{"session_id":"abcdef0123456789","message":"hi"}""")]
    [InlineData("""{"service":"shop","session_id":"x","message":"hi"}""")]
    [InlineData("""{"service":"shop","session_id":"abcdef0123456789","message":"   "}""")]
    [InlineData("""{"service":"shop","session_id":"abcdef0123456789","message":5}""")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public async Task Missing_or_malformed_fields_are_a_bad_request(string body)
    {
        var (status, writer) = await PostAsync(body);
        Assert.Equal(400, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Content_type_must_be_json() =>
        Assert.Equal(415, (await PostAsync(Valid, "text/plain")).Status);

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var (status, writer) = await PostAsync(new string('x', FeedbackEndpoints.MaxBodyBytes + 1));
        Assert.Equal(413, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Long_message_is_truncated()
    {
        var body = $$"""{"service":"shop","session_id":"abcdef0123456789","message":"{{new string('m', FeedbackEndpoints.MaxMessageChars + 50)}}"}""";
        var (_, writer) = await PostAsync(body);
        Assert.Equal(FeedbackEndpoints.MaxMessageChars, Assert.Single(writer.Written).Message.Length);
    }

    [Fact]
    public async Task Service_outside_the_keys_allowlist_is_forbidden()
    {
        var (status, writer) = await PostAsync(Valid, allowed: "other");
        Assert.Equal(403, status);
        Assert.Empty(writer.Written);
    }
}
