using System.Text;
using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Ingest.Tests.Otlp;

public class CheckInEndpointsTests
{
    private sealed class FakeWriter : IClickHouseCheckInWriter
    {
        public List<CheckInRecord> Written { get; } = [];

        public Task WriteAsync(CheckInRecord checkIn, CancellationToken ct = default)
        {
            Written.Add(checkIn);
            return Task.CompletedTask;
        }
    }

    private static async Task<(int Status, FakeWriter Writer)> PingAsync(string slug, string? status = null, string body = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var writer = new FakeWriter();
        var result = await CheckInEndpoints.HandleAsync(slug, status, context, writer, TimeProvider.System, NullLoggerFactory.Instance, CancellationToken.None);
        return (result switch
        {
            NoContent => 204,
            IStatusCodeHttpResult s => s.StatusCode ?? 200,
            _ => 0,
        }, writer);
    }

    [Theory]
    [InlineData(null, "ok")]
    [InlineData("", "ok")]
    [InlineData("start", "start")]
    [InlineData("OK", "ok")]
    [InlineData("Fail", "fail")]
    [InlineData("failed", null)]
    [InlineData("0", null)]
    public void Status_segment_is_parsed(string? segment, string? expected) =>
        Assert.Equal(expected, CheckInEndpoints.ParseStatus(segment));

    [Theory]
    [InlineData("nightly-backup", true)]
    [InlineData("a", true)]
    [InlineData("db_2", true)]
    [InlineData("Nightly", false)]
    [InlineData("-a", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void Slug_shape_is_checked(string slug, bool valid) =>
        Assert.Equal(valid, CheckInEndpoints.IsValidSlug(slug));

    [Fact]
    public async Task Bare_ping_is_an_ok_check_in()
    {
        var (status, writer) = await PingAsync("nightly-backup");

        Assert.Equal(204, status);
        var row = Assert.Single(writer.Written);
        Assert.Equal(("nightly-backup", "ok", ""), (row.Slug, row.Status, row.Message));
    }

    [Fact]
    public async Task Start_and_fail_pings_keep_their_status_and_body_as_the_message()
    {
        var (_, started) = await PingAsync("job", "start");
        var (_, failed) = await PingAsync("job", "fail", "  exit code 2\n");

        Assert.Equal("start", Assert.Single(started.Written).Status);
        var row = Assert.Single(failed.Written);
        Assert.Equal(("fail", "exit code 2"), (row.Status, row.Message));
    }

    [Fact]
    public async Task Long_message_is_truncated()
    {
        var (_, writer) = await PingAsync("job", "fail", new string('m', CheckInEndpoints.MaxMessageChars + 50));
        Assert.Equal(CheckInEndpoints.MaxMessageChars, Assert.Single(writer.Written).Message.Length);
    }

    [Theory]
    [InlineData("Bad Slug", null)]
    [InlineData("job", "finished")]
    public async Task Bad_slug_or_status_is_a_bad_request(string slug, string? segment)
    {
        var (status, writer) = await PingAsync(slug, segment);
        Assert.Equal(400, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var (status, writer) = await PingAsync("job", "fail", new string('x', CheckInEndpoints.MaxBodyBytes + 1));
        Assert.Equal(413, status);
        Assert.Empty(writer.Written);
    }
}
