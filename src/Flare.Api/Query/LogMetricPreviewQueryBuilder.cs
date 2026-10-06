using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built series-count <c>SELECT</c> for <c>POST /api/log-metrics/preview</c>.</summary>
public sealed record LogMetricPreviewSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure builder for the log-metric series preview: counts the stored logs a draft condition
/// matches per <c>(ServiceName, group-by values)</c> combination over <see cref="Window"/>, so
/// the number of rows is the number of series the metric would emit. A key resolves from the log's
/// attributes first and then its resource attributes, as <c>Flare.Ingest</c> does; a log without
/// the key yields an empty value (the metric would omit that dimension).
/// </summary>
public static class LogMetricPreviewQueryBuilder
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Rows read; one more than this is never needed, so reaching it means "at least this many series".</summary>
    public const int MaxSeries = 1000;

    public const int TopSeries = 10;

    public static LogMetricPreviewSql Build(LogMetricPreviewRequest request, DateTimeOffset now)
    {
        // Ingest ignores From/To, so the preview pins its own window instead of the filter's.
        var condition = (request.Condition ?? new LogFilter()) with { From = now - Window, To = null };
        var filterSql = LogFilterSqlBuilder.Build(condition, now);

        var keys = (request.GroupBy ?? []).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var selects = new List<string> { "ServiceName" };
        for (var i = 0; i < keys.Length; i++)
        {
            filterSql.Parameters.AddParameter($"gk{i}", keys[i]);
            selects.Add($"if(mapContains(LogAttributes, {{gk{i}:String}}), LogAttributes[{{gk{i}:String}}], ResourceAttributes[{{gk{i}:String}}])");
        }

        var groupBy = string.Join(", ", Enumerable.Range(1, selects.Count));
        filterSql.Parameters.AddParameter("seriesLimit", MaxSeries);
        var sql = $"SELECT {string.Join(", ", selects)}, count() AS Cnt\n" +
            "FROM logs\n" +
            $"WHERE {filterSql.WhereSql}\n" +
            $"GROUP BY {groupBy}\n" +
            "ORDER BY Cnt DESC\n" +
            "LIMIT {seriesLimit:UInt32}";

        return new LogMetricPreviewSql(sql, filterSql.Parameters);
    }
}
