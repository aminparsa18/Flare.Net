using System.Text;
using Flare.Ingest.Otlp;
using Flare.Ingest.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
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

    private sealed class FakeProvisioner(CheckInProvisionResult result = CheckInProvisionResult.Created) : IClickHouseCheckInProvisioner
    {
        public List<(string Slug, CheckInAutoCreate Spec)> Calls { get; } = [];

        public Task<CheckInProvisionResult> EnsureAsync(string slug, CheckInAutoCreate spec, CancellationToken ct = default)
        {
            Calls.Add((slug, spec));
            return Task.FromResult(result);
        }
    }

    private static async Task<(int Status, FakeWriter Writer)> PingAsync(string slug, string? status = null, string body = "")
    {
        var (code, writer, _) = await PingWithQueryAsync(slug, "", status, body);
        return (code, writer);
    }

    private static async Task<(int Status, FakeWriter Writer, FakeProvisioner Provisioner)> PingWithQueryAsync(
        string slug,
        string query,
        string? status = null,
        string body = "",
        CheckInIngestOptions? options = null,
        CheckInProvisionResult provisioned = CheckInProvisionResult.Created)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.QueryString = new QueryString(query);
        var writer = new FakeWriter();
        var provisioner = new FakeProvisioner(provisioned);
        var result = await CheckInEndpoints.HandleAsync(slug, status, context, writer, provisioner, Options.Create(options ?? new CheckInIngestOptions()), TimeProvider.System, NullLoggerFactory.Instance, CancellationToken.None);
        return (result switch
        {
            NoContent => 204,
            IStatusCodeHttpResult s => s.StatusCode ?? 200,
            _ => 0,
        }, writer, provisioner);
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

    [Fact]
    public async Task Ping_without_create_never_touches_monitors()
    {
        var (status, writer, provisioner) = await PingWithQueryAsync("job", "?cron=0 2 * * *");
        Assert.Equal(204, status);
        Assert.Single(writer.Written);
        Assert.Empty(provisioner.Calls);
    }

    [Fact]
    public async Task Create_with_a_cron_schedule_provisions_the_monitor_then_stores_the_ping()
    {
        var (status, writer, provisioner) = await PingWithQueryAsync("nightly-backup", "?create=1&cron=0%202%20*%20*%20*&tz=Europe/Berlin&grace=600&max_runtime=3600&name=Nightly%20backup", "start");

        Assert.Equal(204, status);
        var (slug, spec) = Assert.Single(provisioner.Calls);
        Assert.Equal("nightly-backup", slug);
        Assert.Equal(new CheckInAutoCreate("Nightly backup", "0 2 * * *", 0, "Europe/Berlin", 600, 3600), spec);
        Assert.Equal("start", Assert.Single(writer.Written).Status);
    }

    [Fact]
    public async Task Create_with_an_interval_uses_defaults_for_the_rest()
    {
        var (status, _, provisioner) = await PingWithQueryAsync("heartbeat", "?create=true&interval=300");

        Assert.Equal(204, status);
        Assert.Equal(new CheckInAutoCreate("heartbeat", "", 300, "UTC", CheckInAutoCreate.DefaultGraceSeconds, 0), Assert.Single(provisioner.Calls).Spec);
    }

    [Theory]
    [InlineData("?create=1")]
    [InlineData("?create=1&cron=0 2 * * *&interval=300")]
    [InlineData("?create=1&cron=nope")]
    [InlineData("?create=1&cron=0 2 * * *&tz=Mars/Base")]
    [InlineData("?create=1&interval=30")]
    [InlineData("?create=1&interval=abc")]
    [InlineData("?create=1&interval=300&grace=-1")]
    [InlineData("?create=1&interval=300&max_runtime=99999999")]
    public async Task Invalid_create_request_is_a_bad_request_and_stores_nothing(string query)
    {
        var (status, writer, provisioner) = await PingWithQueryAsync("job", query);

        Assert.Equal(400, status);
        Assert.Empty(writer.Written);
        Assert.Empty(provisioner.Calls);
    }

    [Fact]
    public async Task Create_is_forbidden_when_auto_create_is_off()
    {
        var (status, writer, provisioner) = await PingWithQueryAsync("job", "?create=1&interval=300", options: new CheckInIngestOptions { AutoCreate = false });

        Assert.Equal(403, status);
        Assert.Empty(writer.Written);
        Assert.Empty(provisioner.Calls);
    }

    [Fact]
    public async Task Create_is_forbidden_at_the_monitor_limit_and_the_ping_is_not_stored()
    {
        var (status, writer, _) = await PingWithQueryAsync("job", "?create=1&interval=300", provisioned: CheckInProvisionResult.LimitReached);

        Assert.Equal(403, status);
        Assert.Empty(writer.Written);
    }

    [Fact]
    public async Task Create_for_an_existing_monitor_still_stores_the_ping()
    {
        var (status, writer, _) = await PingWithQueryAsync("job", "?create=1&interval=300", provisioned: CheckInProvisionResult.Exists);

        Assert.Equal(204, status);
        Assert.Single(writer.Written);
    }

    [Fact]
    public void Monitor_ids_are_stable_per_slug()
    {
        Assert.Equal(ClickHouseCheckInProvisioner.IdFor("job"), ClickHouseCheckInProvisioner.IdFor("job"));
        Assert.NotEqual(ClickHouseCheckInProvisioner.IdFor("job"), ClickHouseCheckInProvisioner.IdFor("other"));
    }
}
