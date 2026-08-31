using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

public class ClickHouseSpanRowMapperTests
{
    [Fact]
    public void Columns_MatchSpansTableColumnOrder()
    {
        // Mirrors db/clickhouse/0007_spans.sql's column declaration order, with the
        // Events Nested column's three desugared array columns listed last.
        Assert.Equal(
            [
                "TraceId", "SpanId", "ParentSpanId", "TraceState", "Name", "Kind",
                "StartTime", "EndTime", "DurationNano", "StatusCode", "StatusMessage",
                "ServiceName", "ResourceSchemaUrl", "ResourceAttributes", "ScopeSchemaUrl",
                "ScopeName", "ScopeVersion", "ScopeAttributes", "SpanAttributes",
                "Events.TimeUnixNano", "Events.Name", "Events.Attributes", "IngestedAt",
            ],
            ClickHouseSpanRowMapper.Columns);
    }

    [Fact]
    public void ToRow_PassesThroughIngestedAt_AsTheLastColumn()
    {
        var ingestedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 5, TimeSpan.Zero);
        var span = MinimalSpan() with { IngestedAt = ingestedAt };

        var row = ClickHouseSpanRowMapper.ToRow(span);

        Assert.Equal(ingestedAt.UtcDateTime, row[22]);
    }

    [Fact]
    public void ToRow_HasOneValuePerColumn()
    {
        var row = ClickHouseSpanRowMapper.ToRow(MinimalSpan());

        Assert.Equal(ClickHouseSpanRowMapper.Columns.Count, row.Length);
    }

    [Fact]
    public void ToRow_CoalescesEveryNullableString_ToEmptyString()
    {
        var row = ClickHouseSpanRowMapper.ToRow(MinimalSpan());

        // Index positions match Columns: ParentSpanId=2, TraceState=3, Name=4,
        // StatusMessage=10, ServiceName=11, ResourceSchemaUrl=12, ScopeSchemaUrl=14,
        // ScopeName=15, ScopeVersion=16.
        foreach (var index in new[] { 2, 3, 4, 10, 11, 12, 14, 15, 16 })
        {
            Assert.Equal(string.Empty, row[index]);
        }
    }

    [Theory]
    [InlineData(0, "STATUS_CODE_UNSET")]
    [InlineData(1, "STATUS_CODE_OK")]
    [InlineData(2, "STATUS_CODE_ERROR")]
    public void ToRow_MapsStatusCode_ToItsEnumLabel(int statusCode, string expectedLabel)
    {
        var span = MinimalSpan() with { StatusCode = statusCode };

        var row = ClickHouseSpanRowMapper.ToRow(span);

        Assert.Equal(expectedLabel, row[9]);
    }

    [Fact]
    public void ToRow_StatusCodeFallsBackToUnset_ForOutOfRangeValues()
    {
        var span = MinimalSpan() with { StatusCode = 99 };

        var row = ClickHouseSpanRowMapper.ToRow(span);

        Assert.Equal("STATUS_CODE_UNSET", row[9]);
    }

    [Fact]
    public void ToRow_CastsKind_ToByte()
    {
        var span = MinimalSpan() with { Kind = 3 };

        var row = ClickHouseSpanRowMapper.ToRow(span);

        Assert.Equal((byte)3, row[5]);
    }

    [Fact]
    public void ToRow_PassesThroughAttributeMaps_AsDictionaries()
    {
        var span = MinimalSpan() with
        {
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "payments-api" },
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            SpanAttributes = new Dictionary<string, string> { ["http.method"] = "POST" },
        };

        var row = ClickHouseSpanRowMapper.ToRow(span);

        Assert.Equal(new Dictionary<string, string> { ["service.name"] = "payments-api" }, (Dictionary<string, string>)row[13]);
        Assert.Equal(new Dictionary<string, string> { ["scope.key"] = "scope-value" }, (Dictionary<string, string>)row[17]);
        Assert.Equal(new Dictionary<string, string> { ["http.method"] = "POST" }, (Dictionary<string, string>)row[18]);
    }

    [Fact]
    public void ToRow_BuildsThreeParallelArrays_FromEvents_InOrder()
    {
        var t1 = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMilliseconds(50);
        var span = MinimalSpan() with
        {
            Events =
            [
                new SpanEvent { Timestamp = t1, Name = "evt.start", Attributes = new Dictionary<string, string> { ["k1"] = "v1" } },
                new SpanEvent { Timestamp = t2, Name = "evt.end", Attributes = new Dictionary<string, string>() },
            ],
        };

        var row = ClickHouseSpanRowMapper.ToRow(span);

        var times = Assert.IsType<DateTime[]>(row[19]);
        var names = Assert.IsType<string[]>(row[20]);
        var attrs = Assert.IsType<Dictionary<string, string>[]>(row[21]);

        Assert.Equal([t1.UtcDateTime, t2.UtcDateTime], times);
        Assert.Equal(["evt.start", "evt.end"], names);
        Assert.Equal(new Dictionary<string, string> { ["k1"] = "v1" }, attrs[0]);
        Assert.Empty(attrs[1]);
    }

    [Fact]
    public void ToRow_BuildsEmptyArrays_WhenSpanHasNoEvents()
    {
        var row = ClickHouseSpanRowMapper.ToRow(MinimalSpan());

        Assert.Empty(Assert.IsType<DateTime[]>(row[19]));
        Assert.Empty(Assert.IsType<string[]>(row[20]));
        Assert.Empty(Assert.IsType<Dictionary<string, string>[]>(row[21]));
    }

    [Fact]
    public void ToRows_MapsEachSpanInOrder()
    {
        var first = MinimalSpan() with { Name = "first" };
        var second = MinimalSpan() with { Name = "second" };

        var rows = ClickHouseSpanRowMapper.ToRows([first, second]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("first", rows[0][4]);
        Assert.Equal("second", rows[1][4]);
    }

    private static SpanRecord MinimalSpan() => new()
    {
        TraceId = "0102030405060708090a0b0c0d0e0f10",
        SpanId = "a1a2a3a4a5a6a7a8",
        Kind = 1,
        StartTime = DateTimeOffset.UnixEpoch,
        EndTime = DateTimeOffset.UnixEpoch.AddMilliseconds(10),
        IngestedAt = DateTimeOffset.UnixEpoch,
        DurationNano = 10_000_000,
        StatusCode = 0,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        SpanAttributes = new Dictionary<string, string>(),
        Events = [],
    };
}
