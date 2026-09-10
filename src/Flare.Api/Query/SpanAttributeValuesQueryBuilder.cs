using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/spans/attribute-values</c>, ready to hand to <see cref="SpanQueryService"/>.</summary>
public sealed record SpanAttributeValuesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanAttributeValuesRequest"/> → parameterized SQL builder - the Traces
/// page's equivalent of <see cref="LogAttributeValuesQueryBuilder"/>, same shape (no
/// subquery needed - see that builder's remarks), not a shared/reused one, same
/// keep-logs-and-spans-independent reasoning <see cref="SpanFilterSqlBuilder"/> gives for
/// not reusing <see cref="LogFilterSqlBuilder"/>.
/// </summary>
public static class SpanAttributeValuesQueryBuilder
{
    public static SpanAttributeValuesSql Build(SpanAttributeValuesRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(request.Key))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Key, "Key must be non-empty.");
        }

        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Limit must be positive.");
        }

        var filterSql = SpanFilterSqlBuilder.Build(request.Filter ?? new SpanFilter(), now);
        var column = SpanFilterSqlBuilder.ColumnFor(request.Bag);

        filterSql.Parameters.AddParameter("valuesKey", request.Key);
        filterSql.Parameters.AddParameter("valuesLimit", request.Limit);

        var whereClauses = new List<string> { filterSql.WhereSql, $"mapContains({column}, {{valuesKey:String}})" };
        if (!string.IsNullOrEmpty(request.Prefix))
        {
            filterSql.Parameters.AddParameter("valuesPrefix", $"%{request.Prefix}%");
            whereClauses.Add($"{column}[{{valuesKey:String}}] ILIKE {{valuesPrefix:String}}");
        }

        var sql = $"SELECT {column}[{{valuesKey:String}}] AS Value, count() AS Cnt\n" +
            "FROM spans\n" +
            $"WHERE {string.Join(" AND ", whereClauses)}\n" +
            "GROUP BY Value\n" +
            "ORDER BY Cnt DESC\n" +
            "LIMIT {valuesLimit:UInt32}";

        return new SpanAttributeValuesSql(sql, filterSql.Parameters);
    }
}
