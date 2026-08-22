using Flare.Api.Model;
using Flare.Api.Query;
using StackExchange.Redis;
using Xunit;

namespace Flare.Api.Tests.Query;

public class IngestionStatsQueryServiceTests
{
    [Theory]
    [InlineData(0, 60)]
    [InlineData(-5, 60)]
    [InlineData(1, 1)]
    [InlineData(1440, 1440)]
    [InlineData(5000, 1440)]
    public void ClampMinutes_ClampsToOneThroughFourteenForty_DefaultingSixtyWhenNonPositive(int requested, int expected)
    {
        Assert.Equal(expected, IngestionStatsQueryService.ClampMinutes(requested));
    }

    [Fact]
    public void BuildBuckets_IsDense_OneEntryPerSignalProtocolPairPerMinute_EvenWhenNoDataExists()
    {
        var buckets = IngestionStatsQueryService.BuildBuckets(startMinute: 1000, minutes: 3, new Dictionary<long, HashEntry[]>());

        Assert.Equal(3 * 3 * 3, buckets.Count); // 3 minutes x 3 signals x 3 protocols
        Assert.All(buckets, b => Assert.Equal(0, b.Requests + b.Records + b.Bytes + b.Rejected));
    }

    [Fact]
    public void BuildBuckets_ReadsFieldsBackForTheMatchingSignalProtocolPrefixOnly()
    {
        var hash = new[]
        {
            new HashEntry("logs:http:requests", 3),
            new HashEntry("logs:http:records", 42),
            new HashEntry("logs:http:bytes", 1234),
            new HashEntry("logs:http:rejected", 1),
            new HashEntry("traces:grpc:requests", 7),
        };
        var minuteHashes = new Dictionary<long, HashEntry[]> { [1000] = hash };

        var buckets = IngestionStatsQueryService.BuildBuckets(startMinute: 1000, minutes: 1, minuteHashes);

        var logsHttp = Assert.Single(buckets, b => b.Signal == IngestionSignal.Logs && b.Protocol == IngestionProtocol.Http);
        Assert.Equal(3, logsHttp.Requests);
        Assert.Equal(42, logsHttp.Records);
        Assert.Equal(1234, logsHttp.Bytes);
        Assert.Equal(1, logsHttp.Rejected);

        var tracesGrpc = Assert.Single(buckets, b => b.Signal == IngestionSignal.Traces && b.Protocol == IngestionProtocol.Grpc);
        Assert.Equal(7, tracesGrpc.Requests);
        Assert.Equal(0, tracesGrpc.Records);

        var metricsHttp = Assert.Single(buckets, b => b.Signal == IngestionSignal.Metrics && b.Protocol == IngestionProtocol.Http);
        Assert.Equal(0, metricsHttp.Requests);
    }

    [Fact]
    public void BuildBuckets_UsesUnixEpochMinuteAsBucketStart()
    {
        var buckets = IngestionStatsQueryService.BuildBuckets(startMinute: 1000, minutes: 1, new Dictionary<long, HashEntry[]>());

        Assert.All(buckets, b => Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000 * 60), b.BucketStart));
    }

    [Fact]
    public void BuildTotals_SumsRequestsAndRejectedAcrossTheWholeWindow()
    {
        var buckets = new[]
        {
            new IngestionBucketPoint(DateTimeOffset.FromUnixTimeSeconds(1000 * 60), IngestionSignal.Logs, IngestionProtocol.Http, Requests: 3, Records: 10, Bytes: 100, Rejected: 1),
            new IngestionBucketPoint(DateTimeOffset.FromUnixTimeSeconds(1001 * 60), IngestionSignal.Logs, IngestionProtocol.Http, Requests: 5, Records: 20, Bytes: 200, Rejected: 2),
        };

        var totals = IngestionStatsQueryService.BuildTotals(buckets, currentMinute: 1001);

        Assert.Equal(8, totals.RequestsInWindow);
        Assert.Equal(3, totals.RejectedInWindow);
    }

    [Fact]
    public void BuildTotals_ArrivalsAndIngestedReflectOnlyTheCurrentMinuteBucket_NotTheWholeWindow()
    {
        var buckets = new[]
        {
            new IngestionBucketPoint(DateTimeOffset.FromUnixTimeSeconds(1000 * 60), IngestionSignal.Logs, IngestionProtocol.Http, Requests: 3, Records: 10, Bytes: 100, Rejected: 0),
            new IngestionBucketPoint(DateTimeOffset.FromUnixTimeSeconds(1001 * 60), IngestionSignal.Logs, IngestionProtocol.Http, Requests: 5, Records: 20, Bytes: 200, Rejected: 0),
            new IngestionBucketPoint(DateTimeOffset.FromUnixTimeSeconds(1001 * 60), IngestionSignal.Traces, IngestionProtocol.Grpc, Requests: 1, Records: 1, Bytes: 5, Rejected: 0),
        };

        var totals = IngestionStatsQueryService.BuildTotals(buckets, currentMinute: 1001);

        Assert.Equal(6, totals.ArrivalsPerMinute); // 5 + 1, minute 1000's 3 excluded
        Assert.Equal(21, totals.IngestedRecordsPerMinute);
        Assert.Equal(205, totals.IngestedBytesPerMinute);
    }

    [Fact]
    public void BuildRecentErrors_ParsesEntriesWrittenInTheIngestSide_PlainPascalCaseFormat()
    {
        var raw = new RedisValue[]
        {
            """{"Timestamp":"2026-08-10T12:00:00+00:00","Signal":"Logs","Protocol":"Http","Reason":"invalid-payload:InvalidProtocolBufferException"}""",
        };

        var errors = IngestionStatsQueryService.BuildRecentErrors(raw);

        var entry = Assert.Single(errors);
        Assert.Equal("Logs", entry.Signal);
        Assert.Equal("Http", entry.Protocol);
        Assert.Equal("invalid-payload:InvalidProtocolBufferException", entry.Reason);
    }

    [Fact]
    public void BuildRecentErrors_SkipsMalformedOrEmptyEntries_WithoutFailingTheWholeBatch()
    {
        var raw = new RedisValue[]
        {
            RedisValue.EmptyString,
            "not valid json",
            """{"Timestamp":"2026-08-10T12:00:00+00:00","Signal":"Logs","Protocol":"Http","Reason":"ok"}""",
        };

        var errors = IngestionStatsQueryService.BuildRecentErrors(raw);

        var entry = Assert.Single(errors);
        Assert.Equal("ok", entry.Reason);
    }
}
