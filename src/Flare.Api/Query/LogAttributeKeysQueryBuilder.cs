using ClickHouse.Driver.ADO.Parameters;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/logs/numeric-attribute-keys</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogAttributeKeysSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogAttributeKeysRequest"/> → parameterized SQL builder: every distinct
/// <c>LogAttributes</c> key that parses as numeric on at least one in-scope event, each
/// with how many do. Populates the Value distribution chart's attribute picker -
/// <see cref="LogValueDistributionQueryBuilder.Build"/> is the query that actually samples
/// a chosen one of these keys.
/// </summary>
/// <remarks>
/// Same <c>arrayJoin(mapKeys(...))</c> shape as
/// <see cref="MetricAttributeKeysQueryBuilder"/> (that one enumerates
/// <c>DataPointAttributes</c> keys the same way), plus a <c>toFloat64OrNull(...) IS NOT
/// NULL</c> filter in the outer query - without it this would list every log attribute key
/// (service names, trace flags, anything string-valued), not just the numeric-looking ones
/// this chart can actually plot.
/// </remarks>
public static class LogAttributeKeysQueryBuilder
{
    public static LogAttributeKeysSql Build(LogAttributeKeysRequest request, DateTimeOffset now)
    {
        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);

        var sql = "SELECT Key, count() AS NumericCount\n" +
            "FROM (\n" +
            "    SELECT arrayJoin(mapKeys(LogAttributes)) AS Key, LogAttributes[Key] AS RawValue\n" +
            "    FROM logs\n" +
            $"    WHERE {filterSql.WhereSql}\n" +
            ")\n" +
            "WHERE toFloat64OrNull(RawValue) IS NOT NULL\n" +
            "GROUP BY Key\n" +
            "ORDER BY NumericCount DESC";

        return new LogAttributeKeysSql(sql, filterSql.Parameters);
    }
}
