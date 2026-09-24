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
/// <para>
/// <b>Gauge:</b> <c>avg(Value)</c> for <see cref="MetricAlertAggregation.Value"/>, <c>min</c>/<c>max</c>
/// for <see cref="MetricAlertAggregation.Min"/>/<see cref="MetricAlertAggregation.Max"/>, and a
/// per-series <c>argMax(Value, Time)</c> averaged across series for
/// <see cref="MetricAlertAggregation.Last"/> (see <see cref="BuildGaugeSql"/>, ADR-0049).
/// </para>
/// <para>
/// <b>Sum:</b> a whole-window, reset-aware <c>increase()</c> for
/// <see cref="MetricAlertAggregation.Value"/>, <c>count()</c> for
/// <see cref="MetricAlertAggregation.Count"/> (see <see cref="BuildSumSql"/>). ADR-0044
/// ports ADR-0035's windowed per-row-delta approach from <see cref="MetricSeriesQueryBuilder"/>'s
/// chart query, collapsed to one scalar: the same <c>row_number()</c>/<c>lagInFrame()</c>
/// CTE partitioned by the full (<c>ServiceName</c>, <c>toString(DataPointAttributes)</c>)
/// counter identity, the same five-way <c>multiIf</c> row classification, then one
/// <c>sum()</c> over every row instead of per bucket. Replaced the old
/// <c>max(Value) - min(Value)</c>, which read a counter reset (process restart) mid-window
/// as a dip or a wrongly-negative value, treated delta-temporality points as cumulative,
/// and - with no per-series partitioning at all - subtracted one series' minimum from a
/// different series' maximum whenever the filter matched more than one.
/// </para>
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
        var (table, whereSql, parameters) = BuildWhere(condition, from, to);
        // `any(Unit)` appended last in every branch, after the type's own aggregate columns
        // - keeps existing ordinals (0/1 for Gauge, 0-1 for Sum, 0-3 for Histogram) stable
        // for AlertQueryService.EvaluateMetricConditionAsync's positional reads, with Unit
        // always the final column regardless of type.
        var sql = condition.Type switch
        {
            MetricPointType.Gauge => BuildGaugeSql(condition.Aggregation, table, whereSql),
            MetricPointType.Sum => BuildSumSql(table, whereSql),
            MetricPointType.Histogram => $"SELECT sum(Count) AS Count, sum(Sum) AS SumTotal, sumForEach(BucketCounts) AS BucketCounts, any(ExplicitBounds) AS ExplicitBounds, any(Unit) AS Unit FROM {table} WHERE {whereSql}",
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition.Type, "Unknown metric point type."),
        };

        return new MetricAlertConditionSql(sql, parameters, condition.Type);
    }

    /// <summary>
    /// A plain <c>count()</c> of the data points <paramref name="condition"/> matches over the
    /// window, for absent-data alerting (<see cref="AlertRule.NoDataWindowSeconds"/>) - the
    /// same <c>WHERE</c> <see cref="Build"/> evaluates over, so "no data" means exactly "nothing
    /// the threshold query could have seen". A dedicated count rather than reading
    /// <see cref="Build"/>'s result for <see cref="double.NaN"/>: only Gauge's <c>avg()</c> yields
    /// NaN over an empty window - Sum's <c>sum()</c> and Histogram's <c>sum(Count)</c> both yield
    /// 0, indistinguishable from a real zero.
    /// </summary>
    public static MetricAlertConditionSql BuildPointCount(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to)
    {
        var (table, whereSql, parameters) = BuildWhere(condition, from, to);
        return new MetricAlertConditionSql($"SELECT count() FROM {table} WHERE {whereSql}", parameters, condition.Type);
    }

    private static (string Table, string WhereSql, ClickHouseParameterCollection Parameters) BuildWhere(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to)
    {
        // Same System.Text.Json init-only-property caveat MetricSeriesQueryBuilder guards
        // against - condition.Filter's `= new()` default doesn't survive deserialization
        // when the persisted/request JSON omits "filter".
        var windowedFilter = (condition.Filter ?? new MetricFilter()) with { From = from, To = to };
        var filterSql = MetricFilterSqlBuilder.Build(windowedFilter, to);
        filterSql.Parameters.AddParameter("metricName", condition.MetricName);
        return (MetricTables.For(condition.Type), $"MetricName = {{metricName:String}} AND {filterSql.WhereSql}", filterSql.Parameters);
    }

    /// <summary>
    /// Gauge's single-scalar query, always <c>Value</c> (Float64) then <c>Unit</c> - the shape
    /// <c>AlertQueryService.EvaluateMetricConditionAsync</c>'s Gauge read expects. An empty window
    /// must read back as NaN ("no data never breaches"): <c>avg()</c> already does, but ClickHouse's
    /// <c>min()</c>/<c>max()</c> over zero rows return the type default <c>0</c>, which would breach a
    /// "disk free below 5%" rule on silence - hence the explicit <c>count() = 0</c> guard.
    /// <see cref="MetricAlertAggregation.Last"/> groups by the same (<c>ServiceName</c>,
    /// <c>toString(DataPointAttributes)</c>) series identity <see cref="BuildSumSql"/> partitions by,
    /// so "last" means each series' latest point rather than whichever series happened to report
    /// most recently; an empty window yields zero inner rows, so the outer <c>avg()</c> is NaN.
    /// </summary>
    private static string BuildGaugeSql(MetricAlertAggregation aggregation, string table, string whereSql) => aggregation switch
    {
        MetricAlertAggregation.Min => $"SELECT if(count() = 0, nan, min(Value)) AS Value, any(Unit) AS Unit FROM {table} WHERE {whereSql}",
        MetricAlertAggregation.Max => $"SELECT if(count() = 0, nan, max(Value)) AS Value, any(Unit) AS Unit FROM {table} WHERE {whereSql}",
        MetricAlertAggregation.Last =>
            "SELECT avg(LastValue) AS Value, any(SeriesUnit) AS Unit FROM (\n" +
            "  SELECT argMax(Value, Time) AS LastValue, any(Unit) AS SeriesUnit\n" +
            $"  FROM {table}\n" +
            $"  WHERE {whereSql}\n" +
            "  GROUP BY ServiceName, toString(DataPointAttributes)\n" +
            ")",
        _ => $"SELECT avg(Value) AS Value, any(Unit) AS Unit FROM {table} WHERE {whereSql}",
    };

    /// <summary>
    /// Sum's whole-window <c>increase()</c> - <see cref="MetricSeriesQueryBuilder"/>'s
    /// <c>BuildSumSql</c> (ADR-0035) minus the bucketing, series grouping and top-N cap.
    /// See that class's "Sum query shape" remarks for why each <c>multiIf</c> branch exists
    /// and why the window always partitions by the full attribute map. Still no
    /// <c>GROUP BY</c> in the outer query, so an empty window still yields exactly one row
    /// (<c>Value</c> 0, <c>Count</c> 0) - same shape the positional reads in
    /// <c>AlertQueryService.EvaluateMetricConditionAsync</c> already expect.
    /// </summary>
    private static string BuildSumSql(string table, string whereSql) =>
        "WITH ranked AS (\n" +
        "  SELECT Value, AggregationTemporality, IsMonotonic, Unit,\n" +
        "    row_number() OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS SeriesRowNum,\n" +
        "    Value - lagInFrame(Value) OVER (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time) AS RawDelta\n" +
        $"  FROM {table}\n" +
        $"  WHERE {whereSql}\n" +
        ")\n" +
        "SELECT sum(multiIf(\n" +
        "    AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA', Value,\n" +
        "    SeriesRowNum = 1, 0,\n" +
        "    IsMonotonic = 0, RawDelta,\n" +
        "    RawDelta < 0, Value,\n" +
        "    RawDelta\n" +
        "  )) AS Value, count() AS Count, any(Unit) AS Unit\n" +
        "FROM ranked";
}
