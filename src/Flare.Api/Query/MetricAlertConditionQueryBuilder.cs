using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built single-row aggregate <c>SELECT</c> for one <see cref="MetricAlertCondition"/> over one evaluation window.</summary>
public sealed record MetricAlertConditionSql(string Sql, ClickHouseParameterCollection Parameters, MetricPointType Type);

/// <summary>
/// Pure <see cref="MetricAlertCondition"/> + window -> parameterized SQL builder, for
/// <c>IAlertQueryService.EvaluateMetricConditionAsync</c>. Deliberately its own builder
/// rather than reusing <see cref="MetricSeriesQueryBuilder"/>: that one buckets by time and
/// groups into one series per distinct (<c>ServiceName</c>, <c>DataPointAttributes</c>) pair
/// for a chart - an alert condition wants exactly one scalar over the whole window,
/// aggregated across everything <see cref="MetricAlertCondition.Filter"/> matches, so there's
/// no bucketing/grouping/top-N dimension to reuse. Shares the same per-type expressions
/// (<see cref="MetricFilterSqlBuilder"/>/<see cref="MetricTables"/>) that
/// <see cref="MetricSeriesQueryBuilder"/> and <see cref="MetricQueryService"/> already use.
/// </summary>
/// <remarks>
/// <para><b>Gauge:</b> <c>avg(Value)</c> - only <see cref="MetricAlertAggregation.Value"/> is meaningful (see that enum's remarks).</para>
/// <para><b>Sum:</b> <c>max(Value) - min(Value)</c> for <see cref="MetricAlertAggregation.Value"/>, <c>count()</c> for <see cref="MetricAlertAggregation.Count"/> - same monotonic-counter approximation caveat <see cref="MetricSeriesQueryBuilder"/>'s remarks document.</para>
/// <para>
/// <b>Histogram:</b> always selects <c>sum(Count)</c>, <c>sum(Sum)</c>, <c>sumForEach(BucketCounts)</c>,
/// <c>any(ExplicitBounds)</c> regardless of <see cref="MetricAlertCondition.Aggregation"/> - the
/// percentile/max-approx aggregations need the raw arrays, not a single column, so
/// <c>IAlertQueryService.EvaluateMetricConditionAsync</c> feeds them through
/// <see cref="HistogramQuantileEstimator.Estimate"/>/<see cref="HistogramQuantileEstimator.EstimateMax"/>
/// in C# afterward, the same "query returns arrays, estimate in C#" split
/// <see cref="MetricQueryService.ReadPoint"/> already uses.
/// </para>
/// </remarks>
public static class MetricAlertConditionQueryBuilder
{
    public static MetricAlertConditionSql Build(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to)
    {
        // Same System.Text.Json init-only-property caveat MetricSeriesQueryBuilder guards
        // against - condition.Filter's `= new()` default doesn't survive deserialization
        // when the persisted/request JSON omits "filter".
        var windowedFilter = (condition.Filter ?? new MetricFilter()) with { From = from, To = to };
        var filterSql = MetricFilterSqlBuilder.Build(windowedFilter, to);
        filterSql.Parameters.AddParameter("metricName", condition.MetricName);

        var table = MetricTables.For(condition.Type);
        var selectExpr = condition.Type switch
        {
            MetricPointType.Gauge => "avg(Value) AS Value",
            MetricPointType.Sum => "max(Value) - min(Value) AS Value, count() AS Count",
            MetricPointType.Histogram => "sum(Count) AS Count, sum(Sum) AS SumTotal, sumForEach(BucketCounts) AS BucketCounts, any(ExplicitBounds) AS ExplicitBounds",
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition.Type, "Unknown metric point type."),
        };

        var sql = $"SELECT {selectExpr} FROM {table} WHERE MetricName = {{metricName:String}} AND {filterSql.WhereSql}";
        return new MetricAlertConditionSql(sql, filterSql.Parameters, condition.Type);
    }
}
