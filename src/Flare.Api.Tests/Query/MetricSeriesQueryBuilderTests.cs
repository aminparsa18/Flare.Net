using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricSeriesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // Same System.Text.Json init-only-property caveat LogAggregateQueryBuilderTests
        // guards against.
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60, Filter = null! }, Now);

        Assert.Contains("WHERE MetricName = {metricName:String} AND Time >=", result.Sql);
    }

    [Fact]
    public void Build_Gauge_SelectsFromGaugeTable_WithAverage()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("avg(Value) AS Value", result.Sql);
        Assert.Equal(MetricPointType.Gauge, result.Type);
    }

    [Fact]
    public void Build_Sum_SelectsFromSumTable_WithMaxMinusMin()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.count", Type = MetricPointType.Sum, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.Contains("max(Value) - min(Value) AS Value", result.Sql);
    }

    [Fact]
    public void Build_Histogram_SelectsFromHistogramTable_WithSumForEachBucketCounts()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "http.server.request.duration", Type = MetricPointType.Histogram, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("FROM metrics_histogram", result.Sql);
        Assert.Contains("sum(Count) AS Count", result.Sql);
        Assert.Contains("sum(Sum) AS SumTotal", result.Sql);
        Assert.Contains("sumForEach(BucketCounts) AS BucketCounts", result.Sql);
        Assert.Contains("any(ExplicitBounds) AS ExplicitBounds", result.Sql);
    }

    [Fact]
    public void Build_GroupsByBucketStartServiceNameAndSeriesKey_OrderedByServiceThenSeriesThenBucket()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 60 }, Now);

        Assert.Contains("toString(DataPointAttributes) AS SeriesKey", result.Sql);
        Assert.Contains("any(DataPointAttributes) AS SeriesAttributes", result.Sql);
        // ServiceName is part of the group key, not just the series key - see the
        // builder's remarks: two services sharing the same (or no) DataPointAttributes
        // must not merge into one series.
        Assert.Contains("GROUP BY BucketStart, ServiceName, SeriesKey", result.Sql);
        Assert.Contains("ORDER BY ServiceName, SeriesKey, BucketStart", result.Sql);
    }

    [Fact]
    public void Build_BindsMetricNameAndBucketWidth()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = 300 }, Now);

        Assert.Contains("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND)", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("process.threads", parameters["metricName"]);
        Assert.Equal(300, parameters["bucketWidth"]);
    }

    [Fact]
    public void Build_AppliesFilter_ServicesAndAttributes()
    {
        var result = MetricSeriesQueryBuilder.Build(
            new MetricQueryRequest
            {
                MetricName = "process.threads",
                Type = MetricPointType.Gauge,
                BucketWidthSeconds = 60,
                Filter = new MetricFilter
                {
                    Services = ["payments-api"],
                    Attributes = [new MetricAttributeFilter { Key = "state", Value = "active" }],
                },
            },
            Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
        Assert.Contains("DataPointAttributes[{attrKey0:String}] = {attrValue0:String}", result.Sql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveBucketWidth_Throws(int bucketWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MetricSeriesQueryBuilder.Build(
                new MetricQueryRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, BucketWidthSeconds = bucketWidth }, Now));
    }
}
