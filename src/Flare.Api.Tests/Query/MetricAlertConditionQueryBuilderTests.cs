using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricAlertConditionQueryBuilderTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 10, 11, 55, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // Same System.Text.Json init-only-property caveat MetricSeriesQueryBuilderTests guards against.
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "process.threads", Type = MetricPointType.Gauge, Filter = null! }, From, To);

        Assert.Contains("WHERE MetricName = {metricName:String} AND Time >=", result.Sql);
    }

    [Fact]
    public void Build_Gauge_SelectsFromGaugeTable_WithAverage_NoGroupBy()
    {
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "process.threads", Type = MetricPointType.Gauge }, From, To);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("avg(Value) AS Value", result.Sql);
        Assert.DoesNotContain("GROUP BY", result.Sql);
        Assert.Equal(MetricPointType.Gauge, result.Type);
    }

    [Fact]
    public void Build_Sum_UsesWindowedResetAwareIncrease_NotMaxMinusMin()
    {
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "http.server.request.count", Type = MetricPointType.Sum }, From, To);

        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.DoesNotContain("max(Value) - min(Value)", result.Sql);
        Assert.Contains("WITH ranked AS (", result.Sql);
        Assert.Contains("lagInFrame(Value) OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS RawDelta", result.Sql);
        Assert.Contains("row_number() OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS SeriesRowNum", result.Sql);
        Assert.Contains("AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA', Value", result.Sql);
        Assert.Contains("RawDelta < 0, Value", result.Sql);
        Assert.Contains(")) AS Value, count() AS Count, any(Unit) AS Unit", result.Sql);
    }

    [Fact]
    public void Build_Sum_IsSingleScalar_NoGroupBy()
    {
        // One whole-window scalar - an empty window must still return exactly one row for
        // AlertQueryService's positional reads, which a GROUP BY would turn into zero rows.
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "http.server.request.count", Type = MetricPointType.Sum }, From, To);

        Assert.DoesNotContain("GROUP BY", result.Sql);
        Assert.Equal(MetricPointType.Sum, result.Type);
    }

    [Fact]
    public void Build_Histogram_SelectsAggregateArrays()
    {
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "http.server.request.duration", Type = MetricPointType.Histogram }, From, To);

        Assert.Contains("FROM metrics_histogram", result.Sql);
        Assert.Contains("sum(Count) AS Count", result.Sql);
        Assert.Contains("sum(Sum) AS SumTotal", result.Sql);
        Assert.Contains("sumForEach(BucketCounts) AS BucketCounts", result.Sql);
        Assert.Contains("any(ExplicitBounds) AS ExplicitBounds", result.Sql);
    }

    [Fact]
    public void Build_BindsMetricNameAndWindow()
    {
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "process.threads", Type = MetricPointType.Gauge }, From, To);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("process.threads", parameters["metricName"]);
        Assert.Equal(From.UtcDateTime, parameters["from"]);
        Assert.Equal(To.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_ServiceFilter_AddsServiceNameClause()
    {
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition
            {
                MetricName = "process.threads",
                Type = MetricPointType.Gauge,
                Filter = new MetricFilter { Services = ["checkout-api"] },
            },
            From,
            To);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
    }
}
