using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure SQL builders behind the attribute-rule preview and unmatched-rule checks (ADR-0083).
/// No ClickHouse dependency; same style and bounded-window reasoning as <see cref="MetricCatalogQueryBuilder"/>.
/// </summary>
public static class MetricAttributeRulePreviewQueryBuilder
{
    /// <summary>Row cap on the preview list.</summary>
    public const int MaxMetrics = 100;

    /// <summary>Cap on distinct metric names read to find unmatched rules.</summary>
    public const int MaxMetricNames = 50_000;

    private static readonly string[] Tables = ["metrics_gauge", "metrics_sum", "metrics_histogram", "metrics_exponential_histogram"];

    /// <summary>The rule's metric pattern as exact-name or prefix, mirroring <c>MetricAttributeReducer.Matches</c>.</summary>
    public static bool Matches(string pattern, string metricName) =>
        pattern.EndsWith('*')
            ? metricName.StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal)
            : string.Equals(pattern, metricName, StringComparison.Ordinal);

    /// <summary>
    /// Per matched metric: series before and after applying the draft rule's attribute filter,
    /// most series removed first, cut at <see cref="MaxMetrics"/> + 1 rows so the caller can tell.
    /// "After" counts distinct (<c>ServiceName</c>, filtered <c>DataPointAttributes</c>) - exactly
    /// the series identity the reducer merges points onto.
    /// </summary>
    public static MetricCatalogSql BuildPreview(MetricAttributeRulePreviewRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = MetricFilterSqlBuilder.Build(new MetricFilter { From = now - TimeSpan.FromMinutes(windowMinutes), To = now }, now);
        var parameters = time.Parameters;
        parameters.AddParameter("attrs", (request.Attributes ?? []).Select(a => a.Trim()).Distinct(StringComparer.Ordinal).ToArray());
        parameters.AddParameter("metricLimit", (uint)(MaxMetrics + 1));

        var metricName = request.MetricName.Trim();
        string metricMatch;
        if (metricName.EndsWith('*'))
        {
            parameters.AddParameter("prefix", metricName[..^1]);
            metricMatch = "startsWith(MetricName, {prefix:String})";
        }
        else
        {
            parameters.AddParameter("metricName", metricName);
            metricMatch = "MetricName = {metricName:String}";
        }

        var keep = request.Mode == MetricAttributeRuleMode.Drop ? "NOT has({attrs:Array(String)}, k)" : "has({attrs:Array(String)}, k)";
        var branches = Tables.Select(table =>
            "  SELECT MetricName, uniq(ServiceName, toString(DataPointAttributes)) AS SeriesBefore, " +
            $"uniq(ServiceName, toString(mapFilter((k, v) -> {keep}, DataPointAttributes))) AS SeriesAfter\n" +
            $"  FROM {table}\n" +
            $"  WHERE {metricMatch} AND {time.WhereSql}\n" +
            "  GROUP BY MetricName\n");

        var sql = "SELECT MetricName, sum(SeriesBefore) AS SeriesBefore, sum(SeriesAfter) AS SeriesAfter\n" +
            "FROM (\n" +
            string.Join("  UNION ALL\n", branches) +
            ")\n" +
            "GROUP BY MetricName\n" +
            "ORDER BY SeriesBefore - SeriesAfter DESC, MetricName\n" +
            "LIMIT {metricLimit:UInt32}";

        return new MetricCatalogSql(sql, parameters);
    }

    /// <summary>Every distinct metric name ingested in the window, across all four tables.</summary>
    public static MetricCatalogSql BuildMetricNames(int windowMinutes, DateTimeOffset now)
    {
        var time = MetricFilterSqlBuilder.Build(new MetricFilter { From = now - TimeSpan.FromMinutes(windowMinutes), To = now }, now);
        var parameters = time.Parameters;
        parameters.AddParameter("nameLimit", (uint)MaxMetricNames);

        var branches = Tables.Select(table => $"  SELECT DISTINCT MetricName FROM {table} WHERE {time.WhereSql}\n");
        var sql = "SELECT MetricName\n" +
            "FROM (\n" +
            string.Join("  UNION ALL\n", branches) +
            ")\n" +
            "GROUP BY MetricName\n" +
            "LIMIT {nameLimit:UInt32}";

        return new MetricCatalogSql(sql, parameters);
    }
}
