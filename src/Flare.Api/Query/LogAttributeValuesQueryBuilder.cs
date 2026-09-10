using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/logs/attribute-values</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogAttributeValuesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogAttributeValuesRequest"/> → parameterized SQL builder: every distinct
/// value one <c>LogAttributes</c>/<c>ResourceAttributes</c>/<c>ScopeAttributes</c> key holds
/// across in-scope events, with how many carry it - the Attribute filters builder's value
/// autocomplete (<c>AttributeFiltersRow.svelte</c>'s value input). Deliberately no subquery
/// (unlike <see cref="LogValueDistributionQueryBuilder"/>'s <c>toFloat64OrNull</c> null
/// check) - <c>mapContains</c> in the outer <c>WHERE</c> already guarantees the map access
/// in <c>SELECT</c>/<c>GROUP BY</c> is well-defined, so there's no alias to smuggle past a
/// same-level <c>WHERE</c> the way that builder's remarks describe.
/// </summary>
public static class LogAttributeValuesQueryBuilder
{
    public static LogAttributeValuesSql Build(LogAttributeValuesRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(request.Key))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Key, "Key must be non-empty.");
        }

        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Limit must be positive.");
        }

        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        var column = LogFilterSqlBuilder.ColumnFor(request.Bag);

        filterSql.Parameters.AddParameter("valuesKey", request.Key);
        filterSql.Parameters.AddParameter("valuesLimit", request.Limit);

        var whereClauses = new List<string> { filterSql.WhereSql, $"mapContains({column}, {{valuesKey:String}})" };
        if (!string.IsNullOrEmpty(request.Prefix))
        {
            // Same substring/case-insensitive ILIKE convention LogFilterSqlBuilder.Build
            // uses for LogFilter.Search - the caller's already-typed text narrows candidates
            // rather than requiring it as an exact/anchored match.
            filterSql.Parameters.AddParameter("valuesPrefix", $"%{request.Prefix}%");
            whereClauses.Add($"{column}[{{valuesKey:String}}] ILIKE {{valuesPrefix:String}}");
        }

        var sql = $"SELECT {column}[{{valuesKey:String}}] AS Value, count() AS Cnt\n" +
            "FROM logs\n" +
            $"WHERE {string.Join(" AND ", whereClauses)}\n" +
            "GROUP BY Value\n" +
            "ORDER BY Cnt DESC\n" +
            "LIMIT {valuesLimit:UInt32}";

        return new LogAttributeValuesSql(sql, filterSql.Parameters);
    }
}
