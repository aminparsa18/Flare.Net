using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

public class ClickHouseMetricRowMapperTests
{
    [Fact]
    public void GaugeColumns_MatchMetricsGaugeTableColumnOrder()
    {
        Assert.Equal(
            [
                "MetricName", "Description", "Unit", "ServiceName", "ResourceSchemaUrl",
                "ResourceAttributes", "ScopeSchemaUrl", "ScopeName", "ScopeVersion",
                "ScopeAttributes", "DataPointAttributes", "StartTime", "Time", "Value",
                "IngestedAt",
            ],
            ClickHouseMetricRowMapper.GaugeColumns);
    }

    [Fact]
    public void SumColumns_MatchMetricsSumTableColumnOrder()
    {
        Assert.Equal(
            [
                "MetricName", "Description", "Unit", "ServiceName", "ResourceSchemaUrl",
                "ResourceAttributes", "ScopeSchemaUrl", "ScopeName", "ScopeVersion",
                "ScopeAttributes", "DataPointAttributes", "StartTime", "Time", "Value",
                "AggregationTemporality", "IsMonotonic", "IngestedAt",
            ],
            ClickHouseMetricRowMapper.SumColumns);
    }

    [Fact]
    public void HistogramColumns_MatchMetricsHistogramTableColumnOrder()
    {
        Assert.Equal(
            [
                "MetricName", "Description", "Unit", "ServiceName", "ResourceSchemaUrl",
                "ResourceAttributes", "ScopeSchemaUrl", "ScopeName", "ScopeVersion",
                "ScopeAttributes", "DataPointAttributes", "StartTime", "Time",
                "AggregationTemporality", "Count", "Sum", "BucketCounts", "ExplicitBounds",
                "IngestedAt",
            ],
            ClickHouseMetricRowMapper.HistogramColumns);
    }

    [Fact]
    public void ToRow_Gauge_PassesThroughIngestedAt_AsTheLastColumn()
    {
        var ingestedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 5, TimeSpan.Zero);
        var row = ClickHouseMetricRowMapper.ToRow(MinimalGauge() with { IngestedAt = ingestedAt });

        // IngestedAt is index 14 in GaugeColumns.
        Assert.Equal(ingestedAt.UtcDateTime, row[14]);
    }

    [Fact]
    public void ToRow_Sum_PassesThroughIngestedAt_AsTheLastColumn()
    {
        var ingestedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 5, TimeSpan.Zero);
        var row = ClickHouseMetricRowMapper.ToRow(MinimalSum() with { IngestedAt = ingestedAt });

        // IngestedAt is index 16 in SumColumns.
        Assert.Equal(ingestedAt.UtcDateTime, row[16]);
    }

    [Fact]
    public void ToRow_Histogram_PassesThroughIngestedAt_AsTheLastColumn()
    {
        var ingestedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 5, TimeSpan.Zero);
        var row = ClickHouseMetricRowMapper.ToRow(MinimalHistogram() with { IngestedAt = ingestedAt });

        // IngestedAt is index 18 in HistogramColumns.
        Assert.Equal(ingestedAt.UtcDateTime, row[18]);
    }

    [Fact]
    public void ToRow_Gauge_HasOneValuePerColumn()
    {
        var row = ClickHouseMetricRowMapper.ToRow(MinimalGauge());

        Assert.Equal(ClickHouseMetricRowMapper.GaugeColumns.Count, row.Length);
    }

    [Fact]
    public void ToRow_Sum_HasOneValuePerColumn()
    {
        var row = ClickHouseMetricRowMapper.ToRow(MinimalSum());

        Assert.Equal(ClickHouseMetricRowMapper.SumColumns.Count, row.Length);
    }

    [Fact]
    public void ToRow_Histogram_HasOneValuePerColumn()
    {
        var row = ClickHouseMetricRowMapper.ToRow(MinimalHistogram());

        Assert.Equal(ClickHouseMetricRowMapper.HistogramColumns.Count, row.Length);
    }

    [Fact]
    public void ToRow_CoalescesEveryNullableString_ToEmptyString()
    {
        var row = ClickHouseMetricRowMapper.ToRow(MinimalGauge());

        // Index positions match GaugeColumns: Description=1, Unit=2, ServiceName=3,
        // ResourceSchemaUrl=4, ScopeSchemaUrl=6, ScopeName=7, ScopeVersion=8.
        foreach (var index in new[] { 1, 2, 3, 4, 6, 7, 8 })
        {
            Assert.Equal(string.Empty, row[index]);
        }
    }

    [Fact]
    public void ToRow_CoalescesNullStartTime_ToTime()
    {
        var time = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var point = MinimalGauge() with { StartTime = null, Time = time };

        var row = ClickHouseMetricRowMapper.ToRow(point);

        // StartTime is index 11, Time is index 12 in GaugeColumns.
        Assert.Equal(time.UtcDateTime, row[11]);
        Assert.Equal(time.UtcDateTime, row[12]);
    }

    [Fact]
    public void ToRow_PreservesExplicitStartTime()
    {
        var start = new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.Zero);
        var time = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var point = MinimalGauge() with { StartTime = start, Time = time };

        var row = ClickHouseMetricRowMapper.ToRow(point);

        Assert.Equal(start.UtcDateTime, row[11]);
        Assert.Equal(time.UtcDateTime, row[12]);
    }

    [Theory]
    [InlineData(0, "AGGREGATION_TEMPORALITY_UNSPECIFIED")]
    [InlineData(1, "AGGREGATION_TEMPORALITY_DELTA")]
    [InlineData(2, "AGGREGATION_TEMPORALITY_CUMULATIVE")]
    public void ToRow_Sum_MapsAggregationTemporality_ToItsEnumLabel(int temporality, string expectedLabel)
    {
        var point = MinimalSum() with { AggregationTemporality = temporality };

        var row = ClickHouseMetricRowMapper.ToRow(point);

        // AggregationTemporality is index 14 in SumColumns.
        Assert.Equal(expectedLabel, row[14]);
    }

    [Fact]
    public void ToRow_Sum_AggregationTemporalityFallsBackToUnspecified_ForOutOfRangeValues()
    {
        var point = MinimalSum() with { AggregationTemporality = 99 };

        var row = ClickHouseMetricRowMapper.ToRow(point);

        Assert.Equal("AGGREGATION_TEMPORALITY_UNSPECIFIED", row[14]);
    }

    [Fact]
    public void ToRow_Sum_CastsIsMonotonic_ToByte()
    {
        var row = ClickHouseMetricRowMapper.ToRow(MinimalSum() with { IsMonotonic = true });

        Assert.Equal((byte)1, row[15]);
    }

    [Fact]
    public void ToRow_Histogram_CoalescesNullSum_ToZero()
    {
        var row = ClickHouseMetricRowMapper.ToRow(MinimalHistogram() with { Sum = null });

        // Sum is index 15 in HistogramColumns.
        Assert.Equal(0d, row[15]);
    }

    [Fact]
    public void ToRow_Histogram_BuildsBucketCountsAndExplicitBoundsArrays()
    {
        var point = MinimalHistogram() with
        {
            BucketCounts = [1UL, 2UL, 3UL],
            ExplicitBounds = [10.0, 50.0],
        };

        var row = ClickHouseMetricRowMapper.ToRow(point);

        // BucketCounts=16, ExplicitBounds=17 in HistogramColumns.
        Assert.Equal(new ulong[] { 1UL, 2UL, 3UL }, Assert.IsType<ulong[]>(row[16]));
        Assert.Equal(new[] { 10.0, 50.0 }, Assert.IsType<double[]>(row[17]));
    }

    [Fact]
    public void ToRow_PassesThroughAttributeMaps_AsDictionaries()
    {
        var point = MinimalGauge() with
        {
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "payments-api" },
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            DataPointAttributes = new Dictionary<string, string> { ["http.route"] = "/checkout" },
        };

        var row = ClickHouseMetricRowMapper.ToRow(point);

        Assert.Equal(new Dictionary<string, string> { ["service.name"] = "payments-api" }, (Dictionary<string, string>)row[5]);
        Assert.Equal(new Dictionary<string, string> { ["scope.key"] = "scope-value" }, (Dictionary<string, string>)row[9]);
        Assert.Equal(new Dictionary<string, string> { ["http.route"] = "/checkout" }, (Dictionary<string, string>)row[10]);
    }

    [Fact]
    public void ToRows_MapsEachPointInOrder()
    {
        var first = MinimalGauge() with { MetricName = "first" };
        var second = MinimalGauge() with { MetricName = "second" };

        var rows = ClickHouseMetricRowMapper.ToRows([first, second]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("first", rows[0][0]);
        Assert.Equal("second", rows[1][0]);
    }

    private static GaugePointRecord MinimalGauge() => new()
    {
        MetricName = "process.threads",
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        DataPointAttributes = new Dictionary<string, string>(),
        Time = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        Value = 1,
    };

    private static SumPointRecord MinimalSum() => new()
    {
        MetricName = "http.server.request.count",
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        DataPointAttributes = new Dictionary<string, string>(),
        Time = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        Value = 1,
        AggregationTemporality = 2,
        IsMonotonic = true,
    };

    private static HistogramPointRecord MinimalHistogram() => new()
    {
        MetricName = "http.server.request.duration",
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        DataPointAttributes = new Dictionary<string, string>(),
        Time = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        AggregationTemporality = 2,
        Count = 0,
        BucketCounts = [],
        ExplicitBounds = [],
    };
}
