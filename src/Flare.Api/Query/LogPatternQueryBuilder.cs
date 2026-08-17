using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY PatternId</c> for <c>/api/logs/patterns</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogPatternSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogPatternRequest"/> → parameterized SQL builder for the ranked
/// Drain-cluster ("log pattern detection") view - same shape/conventions as
/// <see cref="LogAggregateQueryBuilder"/>, grouping by <c>PatternId</c> instead of a
/// time bucket. No materialized view/pre-aggregation, same rationale as that builder's
/// own remarks and <c>db/clickhouse/0010_logs_pattern.sql</c>'s "no skip index in v1" note.
/// </summary>
public static class LogPatternQueryBuilder
{
    public const int DefaultTopN = 200;
    private const int MaxTopN = 1_000;

    /// <summary>OTel's ERROR severity floor (TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16, ERROR 17-20, FATAL 21-24).</summary>
    private const int ErrorSeverityFloor = 17;

    public static LogPatternSql Build(LogPatternRequest request, DateTimeOffset now)
    {
        var topN = Math.Clamp(request.TopN is > 0 ? request.TopN.Value : DefaultTopN, 1, MaxTopN);

        // See LogAggregateQueryBuilder's equivalent comment: request.Filter's default
        // doesn't survive JSON deserialization when "filter" is omitted from the body.
        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        filterSql.Parameters.AddParameter("topN", topN);
        filterSql.Parameters.AddParameter("errorSeverityFloor", ErrorSeverityFloor);
        filterSql.Parameters.AddParameter("emptyPattern", string.Empty);

        var sql = "SELECT PatternId, any(PatternTemplate) AS Template, count() AS Count, " +
            "countIf(SeverityNumber >= {errorSeverityFloor:UInt8}) AS ErrorCount, " +
            "min(Timestamp) AS FirstSeen, max(Timestamp) AS LastSeen\n" +
            "FROM logs\n" +
            $"WHERE {filterSql.WhereSql} AND PatternId != {{emptyPattern:String}}\n" +
            "GROUP BY PatternId\n" +
            "ORDER BY Count DESC\n" +
            "LIMIT {topN:UInt32}";

        return new LogPatternSql(sql, filterSql.Parameters);
    }
}
