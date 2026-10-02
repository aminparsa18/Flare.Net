using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for the Metrics catalog, ready to hand to <see cref="MetricCatalogQueryService"/>.</summary>
public sealed record MetricCatalogSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builders behind <c>POST /api/metrics/catalog</c> (every metric in a window, with
/// cardinality) and <c>POST /api/metrics/catalog/detail</c> (one metric's services, attribute
/// keys, and related-metric candidates). No ClickHouse dependency, same style as
/// <see cref="MetricNamesQueryBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Read straight from the four point-type tables over a bounded recent window, not from a
/// pre-aggregated rollup: the window is capped at <see cref="MaxWindowMinutes"/>, every
/// statement runs under <see cref="QuerySafety"/>'s scan caps, and the catalog is an
/// occasional audit page rather than a panel refreshed every few seconds.
/// </para>
/// <para>
/// Counts use <c>uniq</c>, not <c>count(DISTINCT ...)</c> like <see cref="MetricNamesQueryBuilder"/>:
/// the point of this page is finding the metric whose series count has exploded, which is
/// exactly the case where an exact distinct count's memory grows with the answer. <c>uniq</c>
/// is exact at small cardinalities and bounded-memory at large ones.
/// </para>
/// </remarks>
public static class MetricCatalogQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    /// <summary>Row cap on the catalog list.</summary>
    public const int MaxMetrics = 1000;

    public const int MaxServices = 100;
    public const int MaxAttributeKeys = 200;

    /// <summary>How many of each attribute key's most frequent values the detail view shows.</summary>
    public const int SampleValueCount = 5;

    /// <summary>Metrics considered when ranking related metrics - bounds the one statement that reads every table.</summary>
    public const int MaxRelatedCandidates = 2000;

    public const int MaxRelated = 10;

    /// <summary>Per-metric cap on the service/attribute-key sets collected for related-metric ranking.</summary>
    private const int MaxCandidateSetSize = 200;

    private static readonly (string Table, string Type)[] Tables =
    [
        ("metrics_gauge", "gauge"),
        ("metrics_sum", "sum"),
        ("metrics_histogram", "histogram"),
        ("metrics_exponential_histogram", "exponential_histogram"),
    ];

    /// <summary>Same default/clamp shape as <see cref="HostInventoryQueryBuilder.ClampWindowMinutes"/>.</summary>
    public static int ClampWindowMinutes(int? requested) =>
        Math.Clamp(requested is > 0 ? requested.Value : DefaultWindowMinutes, MinWindowMinutes, MaxWindowMinutes);

    /// <summary>
    /// One row per metric name across all four tables, highest series count first, capped at
    /// <see cref="MaxMetrics"/> + 1 rows so the caller can tell the list was cut.
    /// </summary>
    /// <remarks>
    /// Series identity is (<c>ServiceName</c>, <c>DataPointAttributes</c>), matching
    /// <see cref="MetricSeriesQueryBuilder"/>'s one-line-per-series grouping; the Map goes
    /// through <c>toString</c> for the same reason <see cref="MetricNamesQueryBuilder"/>'s
    /// <c>SeriesCount</c> does. Ordered and cut server-side by series count so truncation
    /// drops the least interesting rows, not an alphabetical tail.
    /// </remarks>
    public static MetricCatalogSql BuildCatalog(MetricCatalogRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("metricLimit", (uint)(MaxMetrics + 1));

        var where = time.WhereSql;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            where += " AND positionCaseInsensitiveUTF8(MetricName, {search:String}) > 0";
        }

        var branches = Tables.Select(t =>
            $"  SELECT MetricName, '{t.Type}' AS Type, argMaxIf(Unit, Time, Unit != '') AS Unit, argMaxIf(Description, Time, Description != '') AS Description, " +
            "uniq(ServiceName) AS ServiceCount, uniq(ServiceName, toString(DataPointAttributes)) AS SeriesCount, count() AS SampleCount, max(Time) AS LastReceived\n" +
            $"  FROM {t.Table}\n" +
            $"  WHERE {where}\n" +
            "  GROUP BY MetricName\n");

        var sql = "SELECT MetricName, Type, Unit, Description, ServiceCount, SeriesCount, SampleCount, LastReceived\n" +
            "FROM (\n" +
            string.Join("  UNION ALL\n", branches) +
            ")\n" +
            "ORDER BY SeriesCount DESC, MetricName\n" +
            "LIMIT {metricLimit:UInt32}";

        return new MetricCatalogSql(sql, parameters);
    }

    /// <summary>The metric's per-service series/sample counts, plus its unit/description (columns 1-2).</summary>
    public static MetricCatalogSql BuildServices(MetricCatalogDetailRequest request, int windowMinutes, DateTimeOffset now)
    {
        var (where, parameters) = MetricScope(request, windowMinutes, now);
        parameters.AddParameter("serviceLimit", (uint)MaxServices);

        var sql = "SELECT ServiceName, argMaxIf(Unit, Time, Unit != '') AS Unit, argMaxIf(Description, Time, Description != '') AS Description, " +
            "uniq(toString(DataPointAttributes)) AS SeriesCount, count() AS SampleCount, max(Time) AS LastReceived\n" +
            $"FROM {MetricTables.For(request.Type)}\n" +
            $"WHERE {where}\n" +
            "GROUP BY ServiceName\n" +
            "ORDER BY SeriesCount DESC, ServiceName\n" +
            "LIMIT {serviceLimit:UInt32}";

        return new MetricCatalogSql(sql, parameters);
    }

    /// <summary>
    /// Every <c>DataPointAttributes</c> key on the metric with its distinct-value count, how
    /// many points carry it, and its most frequent values. Same <c>arrayJoin(mapKeys(...))</c>
    /// expansion as <see cref="MetricAttributeKeysQueryBuilder"/>.
    /// </summary>
    public static MetricCatalogSql BuildAttributes(MetricCatalogDetailRequest request, int windowMinutes, DateTimeOffset now)
    {
        var (where, parameters) = MetricScope(request, windowMinutes, now);
        parameters.AddParameter("keyLimit", (uint)MaxAttributeKeys);

        var sql = $"SELECT Key, uniq(Value) AS DistinctValueCount, count() AS SampleCount, topK({SampleValueCount})(Value) AS SampleValues\n" +
            "FROM (\n" +
            "    SELECT arrayJoin(mapKeys(DataPointAttributes)) AS Key, DataPointAttributes[Key] AS Value\n" +
            $"    FROM {MetricTables.For(request.Type)}\n" +
            $"    WHERE {where}\n" +
            ")\n" +
            "GROUP BY Key\n" +
            "ORDER BY DistinctValueCount DESC, Key\n" +
            "LIMIT {keyLimit:UInt32}";

        return new MetricCatalogSql(sql, parameters);
    }

    /// <summary>
    /// Every metric in the window (the target included) with its service set and attribute-key
    /// set - the input <see cref="MetricRelatedRanker"/> scores against the target.
    /// </summary>
    public static MetricCatalogSql BuildRelatedCandidates(int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("candidateLimit", (uint)MaxRelatedCandidates);

        var branches = Tables.Select(t =>
            $"  SELECT MetricName, '{t.Type}' AS Type, groupUniqArray({MaxCandidateSetSize})(ServiceName) AS Services, " +
            $"groupUniqArrayArray({MaxCandidateSetSize})(mapKeys(DataPointAttributes)) AS AttributeKeys\n" +
            $"  FROM {t.Table}\n" +
            $"  WHERE {time.WhereSql}\n" +
            "  GROUP BY MetricName\n");

        var sql = "SELECT MetricName, Type, Services, AttributeKeys\n" +
            "FROM (\n" +
            string.Join("  UNION ALL\n", branches) +
            ")\n" +
            "ORDER BY MetricName\n" +
            "LIMIT {candidateLimit:UInt32}";

        return new MetricCatalogSql(sql, parameters);
    }

    private static (string Where, ClickHouseParameterCollection Parameters) MetricScope(MetricCatalogDetailRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("metricName", request.MetricName);
        return ($"MetricName = {{metricName:String}} AND {time.WhereSql}", time.Parameters);
    }

    private static MetricFilterSql TimeFilter(int windowMinutes, DateTimeOffset now) =>
        MetricFilterSqlBuilder.Build(new MetricFilter { From = now - TimeSpan.FromMinutes(windowMinutes), To = now }, now);
}
