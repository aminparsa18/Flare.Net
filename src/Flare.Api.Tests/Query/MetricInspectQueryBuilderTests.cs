using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricInspectQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static MetricCatalogInspectRequest Request(MetricPointType type, string? service = null) =>
        new() { MetricName = "http.server.active_requests", Type = type, ServiceName = service };

    [Theory]
    [InlineData(null, MetricInspectQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, MetricInspectQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, MetricInspectQueryBuilder.MinWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(1440, MetricInspectQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps(int? requested, int expected)
    {
        Assert.Equal(expected, MetricInspectQueryBuilder.ClampWindowMinutes(requested));
    }

    [Theory]
    [InlineData(null, 15, MetricInspectQueryBuilder.DefaultBucketWidthSeconds)]
    [InlineData(1, 5, MetricInspectQueryBuilder.MinBucketWidthSeconds)]
    [InlineData(10, 60, 60)] // 60 buckets max over an hour: at least 60s
    [InlineData(3600, 15, 900)] // never wider than the window
    [InlineData(120, 15, 120)]
    public void ClampBucketWidthSeconds_DefaultsAndClamps(int? requested, int windowMinutes, int expected)
    {
        Assert.Equal(expected, MetricInspectQueryBuilder.ClampBucketWidthSeconds(requested, windowMinutes));
    }

    [Theory]
    [InlineData(MetricPointType.Gauge, "metrics_gauge", "Value AS Value", "toUInt8(0) AS IsDelta", "toUInt8(0) AS IsMonotonic")]
    [InlineData(MetricPointType.Sum, "metrics_sum", "Value AS Value", "toUInt8(AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA') AS IsDelta", "toUInt8(IsMonotonic) AS IsMonotonic")]
    [InlineData(MetricPointType.Histogram, "metrics_histogram", "toFloat64(Count) AS Value", "toUInt8(AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA') AS IsDelta", "toUInt8(1) AS IsMonotonic")]
    [InlineData(MetricPointType.ExponentialHistogram, "metrics_exponential_histogram", "toFloat64(Count) AS Value", "toUInt8(AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA') AS IsDelta", "toUInt8(1) AS IsMonotonic")]
    public void BuildSamples_ReadsTheTypesValueAndTemporality(MetricPointType type, string table, string value, string isDelta, string isMonotonic)
    {
        var sql = MetricInspectQueryBuilder.BuildSamples(Request(type), 15, Now).Sql;

        Assert.Contains($"FROM {table}\n", sql);
        Assert.Contains(value, sql);
        Assert.Contains(isDelta, sql);
        Assert.Contains(isMonotonic, sql);
    }

    [Fact]
    public void BuildSamples_PicksTheBusiestSeries_AndCapsSamplesPerSeriesPlusOne()
    {
        var built = MetricInspectQueryBuilder.BuildSamples(Request(MetricPointType.Gauge), 15, Now);

        Assert.Contains("ORDER BY Samples DESC, ServiceName, SeriesKey", built.Sql);
        Assert.Contains("ORDER BY ServiceName, SeriesKey, Time DESC", built.Sql);
        Assert.Contains("LIMIT {sampleLimit:UInt32} BY ServiceName, SeriesKey", built.Sql);
        Assert.Equal((uint)MetricInspectQueryBuilder.MaxSeries, built.Parameters["seriesLimit"].Value);
        Assert.Equal((uint)(MetricInspectQueryBuilder.MaxSamplesPerSeries + 1), built.Parameters["sampleLimit"].Value);
    }

    [Fact]
    public void BuildSamples_ScopesToMetricWindowAndService()
    {
        var built = MetricInspectQueryBuilder.BuildSamples(Request(MetricPointType.Sum, "checkout"), 15, Now);

        Assert.Contains("MetricName = {metricName:String}", built.Sql);
        Assert.Contains("ServiceName IN {services:Array(String)}", built.Sql);
        Assert.Equal(new[] { "checkout" }, built.Parameters["services"].Value);
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, built.Parameters["from"].Value);
    }

    [Fact]
    public void BuildSamples_WithoutService_HasNoServiceFilter()
    {
        var sql = MetricInspectQueryBuilder.BuildSamples(Request(MetricPointType.Sum, " "), 15, Now).Sql;

        Assert.DoesNotContain("ServiceName IN", sql);
    }

    [Fact]
    public void BuildSeriesCount_CountsServiceAndAttributeSets()
    {
        var sql = MetricInspectQueryBuilder.BuildSeriesCount(Request(MetricPointType.Gauge), 15, Now).Sql;

        Assert.Contains("uniq(ServiceName, toString(DataPointAttributes))", sql);
        Assert.Contains("FROM metrics_gauge", sql);
    }
}
