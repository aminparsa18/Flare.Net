using System.Text;
using Flare.Ingest.Auth;
using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Ingest.Tests.Otlp;

public class WebReplayEndpointsTests
{
    private sealed class FakeWriter : IClickHouseWebReplayWriter
    {
        public List<WebReplayRecord> Written { get; } = [];
        public Task WriteAsync(WebReplayRecord chunk, DateTimeOffset at, CancellationToken ct = default)
        {
            Written.Add(chunk);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly long NowMs = Now.ToUnixTimeMilliseconds();
    private const string DefaultQuery = "?service=shop-web&session_id=abcdef0123456789";

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private static string Events(params long[] timestamps) =>
        "[" + string.Join(",", timestamps.Select(t => $"{{\"type\":3,\"timestamp\":{t},\"data\":{{\"source\":1}}}}")) + "]";

    private static async Task<(int Status, FakeWriter Writer)> PostAsync(
        byte[] body, string contentType = "application/json", string query = DefaultQuery, params string[] allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(body);
        context.Request.QueryString = new QueryString(query);
        if (allowed.Length > 0)
            context.Features.Set(new IngestKeyUsageFeature(Guid.NewGuid()) { AllowedServices = allowed.ToHashSet() });

        var writer = new FakeWriter();
        var result = await WebReplayEndpoints.HandleAsync(context, writer, new FixedClock(Now), NullLoggerFactory.Instance, CancellationToken.None);
        var status = result switch
        {
            NoContent => 204,
            IStatusCodeHttpResult s => s.StatusCode ?? 200,
            _ => 0,
        };
        return (status, writer);
    }

    [Fact]
    public async Task Valid_batch_is_stored_with_its_event_count_and_times()
    {
        var (status, writer) = await PostAsync(Bytes(Events(NowMs - 5000, NowMs - 2000, NowMs - 4000)));

        Assert.Equal(204, status);
        var row = Assert.Single(writer.Written);
        Assert.Equal(("shop-web", "abcdef0123456789", 3), (row.ServiceName, row.SessionId, row.EventCount));
        Assert.Equal(Now.AddSeconds(-5), row.StartTime);
        Assert.Equal(Now.AddSeconds(-2), row.EndTime);
        Assert.NotEqual(Guid.Empty, row.ChunkId);
    }

    [Fact]
    public async Task Payload_is_stored_as_received()
    {
        var json = Events(NowMs);
        var (_, writer) = await PostAsync(Bytes(json));
        Assert.Equal(json, Encoding.UTF8.GetString(Assert.Single(writer.Written).Payload));
    }

    [Theory]
    [InlineData("text/plain", 415)]
    [InlineData("application/json; charset=utf-8", 204)]
    public async Task Content_type_must_be_json(string contentType, int expected) =>
        Assert.Equal(expected, (await PostAsync(Bytes(Events(NowMs)), contentType)).Status);

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var (status, writer) = await PostAsync(new byte[WebReplayEndpoints.MaxPayloadBytes + 1]);
        Assert.Equal(413, status);
        Assert.Empty(writer.Written);
    }

    [Theory]
    [InlineData("?session_id=abcdef0123456789")]
    [InlineData("?service=shop-web")]
    [InlineData("?service=shop-web&session_id=short")]
    [InlineData("?service=shop-web&session_id=has spaces in it")]
    public async Task Missing_or_malformed_ids_are_a_bad_request(string query)
    {
        var (status, writer) = await PostAsync(Bytes(Events(NowMs)), query: query);
        Assert.Equal(400, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Service_outside_the_keys_allowlist_is_forbidden()
    {
        var (status, writer) = await PostAsync(Bytes(Events(NowMs)), allowed: "other");
        Assert.Equal(403, status);
        Assert.Empty(writer.Written);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[1,2]")]
    [InlineData("[{\"type\":3}]")]
    [InlineData("[{\"timestamp\":1}]")]
    [InlineData("[{\"type\":\"3\",\"timestamp\":1}]")]
    [InlineData("[{\"type\":3,\"timestamp\":\"1\"}]")]
    [InlineData("[{\"type\":3,\"timestamp\":1}")]
    [InlineData("[{\"type\":3,\"timestamp\":1}] []")]
    public async Task Anything_but_an_array_of_events_is_a_bad_request(string json)
    {
        var (status, writer) = await PostAsync(Bytes(json));
        Assert.Equal(400, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Times_far_from_the_server_clock_are_clamped()
    {
        var (_, writer) = await PostAsync(Bytes(Events(1, NowMs + 3_600_000)));

        var row = Assert.Single(writer.Written);
        Assert.Equal(Now - WebReplayEndpoints.MaxPast, row.StartTime);
        Assert.Equal(Now + WebReplayEndpoints.MaxFuture, row.EndTime);
    }

    [Fact]
    public void Inspect_reads_type_and_timestamp_only_at_the_events_own_level()
    {
        // A nested data object has its own "timestamp" and "type"; neither may be counted or mistaken for the event's.
        var json = """[{"type":2,"timestamp":100,"data":{"timestamp":999999,"type":"nested","node":{"childNodes":[{"type":2}]}}},{"timestamp":200,"type":4}]""";

        var summary = WebReplayEndpoints.Inspect(Bytes(json), out _);

        Assert.Equal(new WebReplayEndpoints.ChunkSummary(2, 100, 200), summary);
    }

    [Fact]
    public void Inspect_accepts_a_deeply_nested_snapshot()
    {
        var depth = 300;
        var nested = string.Concat(Enumerable.Repeat("{\"childNodes\":[", depth)) + "{}" + string.Concat(Enumerable.Repeat("]}", depth));
        var json = $"[{{\"type\":2,\"timestamp\":5,\"data\":{nested}}}]";

        Assert.Equal(new WebReplayEndpoints.ChunkSummary(1, 5, 5), WebReplayEndpoints.Inspect(Bytes(json), out _));
    }

    [Fact]
    public void Inspect_accepts_fractional_timestamps()
    {
        var summary = WebReplayEndpoints.Inspect(Bytes("""[{"type":3,"timestamp":1700000000000.5}]"""), out _);
        Assert.Equal(1_700_000_000_000, summary?.FirstUnixMs);
    }
}
