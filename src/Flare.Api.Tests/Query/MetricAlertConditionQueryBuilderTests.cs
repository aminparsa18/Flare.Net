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

    [Theory]
    [InlineData(MetricAlertAggregation.Min, "if(count() = 0, nan, min(Value)) AS Value")]
    [InlineData(MetricAlertAggregation.Max, "if(count() = 0, nan, max(Value)) AS Value")]
    public void Build_Gauge_MinMax_GuardEmptyWindowToNaN(MetricAlertAggregation aggregation, string expected)
    {
        // min()/max() over zero rows return 0, not NaN - unguarded, a silent "< 5%" rule would fire.
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "system.filesystem.utilization", Type = MetricPointType.Gauge, Aggregation = aggregation }, From, To);

        Assert.Contains(expected, result.Sql);
        Assert.Contains("any(Unit) AS Unit FROM metrics_gauge", result.Sql);
        Assert.DoesNotContain("GROUP BY", result.Sql);
    }

    [Fact]
    public void Build_Gauge_Last_TakesEachSeriesLatestPoint_AveragedAcrossSeries()
    {
        var result = MetricAlertConditionQueryBuilder.Build(
            new MetricAlertCondition { MetricName = "system.filesystem.utilization", Type = MetricPointType.Gauge, Aggregation = MetricAlertAggregation.Last }, From, To);

        Assert.Contains("argMax(Value, Time) AS LastValue", result.Sql);
        Assert.Contains("GROUP BY ServiceName, toString(DataPointAttributes)", result.Sql);
        Assert.StartsWith("SELECT avg(LastValue) AS Value, any(SeriesUnit) AS Unit FROM (", result.Sql);
        Assert.Contains("WHERE MetricName = {metricName:String}", result.Sql);
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

    [Theory]
    [InlineData(MetricPointType.Gauge, "metrics_gauge")]
    [InlineData(MetricPointType.Sum, "metrics_sum")]
    [InlineData(MetricPointType.Histogram, "metrics_histogram")]
    public void BuildPointCount_CountsRawPointsInTheTypesTable(MetricPointType type, string table)
    {
        var result = MetricAlertConditionQueryBuilder.BuildPointCount(
            new MetricAlertCondition { MetricName = "http.server.request.duration", Type = type }, From, To);

        Assert.StartsWith($"SELECT count() FROM {table} WHERE MetricName = {{metricName:String}} AND ", result.Sql);
        Assert.Equal(type, result.Type);
    }

    [Fact]
    public void BuildPointCount_UsesTheSameWhereAsBuild()
    {
        // "No data" must mean exactly "nothing the threshold query could have seen".
        var condition = new MetricAlertCondition
        {
            MetricName = "process.threads",
            Type = MetricPointType.Gauge,
            Filter = new MetricFilter { Services = ["checkout"] },
        };

        var evaluate = MetricAlertConditionQueryBuilder.Build(condition, From, To);
        var count = MetricAlertConditionQueryBuilder.BuildPointCount(condition, From, To);

        var evaluateWhere = evaluate.Sql[evaluate.Sql.IndexOf(" WHERE ", StringComparison.Ordinal)..];
        var countWhere = count.Sql[count.Sql.IndexOf(" WHERE ", StringComparison.Ordinal)..];
        Assert.Equal(evaluateWhere, countWhere);
    }
}
