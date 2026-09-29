using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure SQL builders behind <c>POST /api/metrics/catalog/inspect</c>: the raw samples of a
/// metric's busiest few series, which <see cref="MetricInspectReducer"/> then reduces the way
/// the Metrics explorer does. No ClickHouse dependency, same style as
/// <see cref="MetricCatalogQueryBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Raw rows, not <see cref="MetricSeriesQueryBuilder"/>'s already-bucketed output: the point
/// of the view is showing each step, so the reduction runs app-side over exactly the samples
/// on screen. Doing it there rather than in a second ClickHouse query also keeps the steps
/// consistent with each other when a series' sample list is capped.
/// </para>
/// <para>
/// Series are picked by sample count in the window (ties by name, so the pick is stable
/// between refreshes) - the busiest series show the most reduction per bucket. Samples are
/// cut per series with <c>LIMIT n BY</c> over the newest-first order, so a capped series
/// keeps its most recent samples; one extra row per series tells the caller it was capped.
/// </para>
/// </remarks>
public static class MetricInspectQueryBuilder
{
    public const int DefaultWindowMinutes = 15;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 60;

    public const int DefaultBucketWidthSeconds = 60;
    public const int MinBucketWidthSeconds = 10;

    /// <summary>Upper bound on buckets in the window, so a narrow bucket over a wide window stays readable.</summary>
    public const int MaxBuckets = 60;

    public const int MaxSeries = 5;

    /// <summary>One sample every 5s for the full <see cref="MaxWindowMinutes"/>.</summary>
    public const int MaxSamplesPerSeries = 720;

    private const string DeltaExpr = "AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA'";

    public static int ClampWindowMinutes(int? requested) =>
        Math.Clamp(requested is > 0 ? requested.Value : DefaultWindowMinutes, MinWindowMinutes, MaxWindowMinutes);

    /// <summary>
    /// Clamped to at least <see cref="MinBucketWidthSeconds"/>, at least 1/<see cref="MaxBuckets"/>
    /// of the window, and at most the window itself.
    /// </summary>
    public static int ClampBucketWidthSeconds(int? requested, int windowMinutes)
    {
        var windowSeconds = windowMinutes * 60;
        var min = Math.Max(MinBucketWidthSeconds, (windowSeconds + MaxBuckets - 1) / MaxBuckets);
        return Math.Clamp(requested is > 0 ? requested.Value : DefaultBucketWidthSeconds, min, windowSeconds);
    }

    /// <summary>
    /// Each picked series' newest <see cref="MaxSamplesPerSeries"/> + 1 samples, grouped by
    /// series and newest first within one: <c>ServiceName</c>, <c>SeriesKey</c>,
    /// <c>DataPointAttributes</c>, <c>Time</c>, <c>Value</c>, <c>IsDelta</c>, <c>IsMonotonic</c>.
    /// </summary>
    /// <remarks>
    /// Gauge has no temporality columns, so those are literals. A histogram's <c>Count</c> is
    /// read as the value and treated as monotonic - an observation count never goes down except
    /// on a restart.
    /// </remarks>
    public static MetricCatalogSql BuildSamples(MetricCatalogInspectRequest request, int windowMinutes, DateTimeOffset now)
    {
        var (where, parameters) = Scope(request, windowMinutes, now);
        parameters.AddParameter("seriesLimit", (uint)MaxSeries);
        parameters.AddParameter("sampleLimit", (uint)(MaxSamplesPerSeries + 1));

        var table = MetricTables.For(request.Type);
        var (value, isDelta, isMonotonic) = request.Type switch
        {
            MetricPointType.Gauge => ("Value", "0", "0"),
            MetricPointType.Sum => ("Value", DeltaExpr, "IsMonotonic"),
            MetricPointType.Histogram or MetricPointType.ExponentialHistogram => ("toFloat64(Count)", DeltaExpr, "1"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unknown metric point type."),
        };

        var sql = $"SELECT ServiceName, toString(DataPointAttributes) AS SeriesKey, DataPointAttributes, Time, {value} AS Value, toUInt8({isDelta}) AS IsDelta, toUInt8({isMonotonic}) AS IsMonotonic\n" +
            $"FROM {table}\n" +
            $"WHERE {where}\n" +
            "  AND (ServiceName, toString(DataPointAttributes)) GLOBAL IN (\n" +
            "    SELECT ServiceName, SeriesKey FROM (\n" +
            "      SELECT ServiceName, toString(DataPointAttributes) AS SeriesKey, count() AS Samples\n" +
            $"      FROM {table}\n" +
            $"      WHERE {where}\n" +
            "      GROUP BY ServiceName, SeriesKey\n" +
            "      ORDER BY Samples DESC, ServiceName, SeriesKey\n" +
            "      LIMIT {seriesLimit:UInt32}\n" +
            "    )\n" +
            "  )\n" +
            "ORDER BY ServiceName, SeriesKey, Time DESC\n" +
            "LIMIT {sampleLimit:UInt32} BY ServiceName, SeriesKey";

        return new MetricCatalogSql(sql, parameters);
    }

    /// <summary>How many series the metric has in scope - one <c>uniq</c> row, so the view can say "5 of N".</summary>
    public static MetricCatalogSql BuildSeriesCount(MetricCatalogInspectRequest request, int windowMinutes, DateTimeOffset now)
    {
        var (where, parameters) = Scope(request, windowMinutes, now);
        var sql = "SELECT uniq(ServiceName, toString(DataPointAttributes))\n" +
            $"FROM {MetricTables.For(request.Type)}\n" +
            $"WHERE {where}";
        return new MetricCatalogSql(sql, parameters);
    }

    private static (string Where, ClickHouseParameterCollection Parameters) Scope(MetricCatalogInspectRequest request, int windowMinutes, DateTimeOffset now)
    {
        var filter = new MetricFilter
        {
            From = now - TimeSpan.FromMinutes(windowMinutes),
            To = now,
            Services = string.IsNullOrWhiteSpace(request.ServiceName) ? null : [request.ServiceName],
        };
        var built = MetricFilterSqlBuilder.Build(filter, now);
        built.Parameters.AddParameter("metricName", request.MetricName);
        return ($"MetricName = {{metricName:String}} AND {built.WhereSql}", built.Parameters);
    }
}
