using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/errors/facet-values</c>.</summary>
public sealed record ExceptionFacetValuesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="ExceptionFacetValuesRequest"/> → parameterized SQL - the /errors facet
/// sidebar's value counts. Same <c>ARRAY JOIN</c> and <see cref="ExceptionFilterSqlBuilder"/>
/// <c>WHERE</c> as <see cref="ExceptionGroupQueryBuilder"/>, including its
/// <c>exception.type != ''</c> guard, so a facet's counts add up to what the groups table
/// shows rather than counting untyped exception events the table never lists.
/// </summary>
public static class ExceptionFacetValuesQueryBuilder
{
    public const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    public static ExceptionFacetValuesSql Build(ExceptionFacetValuesRequest request, DateTimeOffset now)
    {
        if (request.Field == ExceptionFacetField.ResourceAttribute && string.IsNullOrEmpty(request.Key))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Key, "Key must be non-empty for a ResourceAttribute facet.");
        }

        var filterSql = ExceptionFilterSqlBuilder.Build(request.Filter ?? new ExceptionFilter(), now);
        var limit = Math.Clamp(request.Limit is > 0 ? request.Limit.Value : DefaultLimit, 1, MaxLimit);
        filterSql.Parameters.AddParameter("valuesLimit", (uint)limit);

        var whereClauses = new List<string> { filterSql.WhereSql, "EventAttributes['exception.type'] != ''" };
        string valueSql;
        if (request.Field == ExceptionFacetField.ResourceAttribute)
        {
            filterSql.Parameters.AddParameter("valuesKey", request.Key);
            valueSql = "ResourceAttributes[{valuesKey:String}]";
            whereClauses.Add("mapContains(ResourceAttributes, {valuesKey:String})");
        }
        else
        {
            valueSql = "ServiceName";
        }

        var sql = $"SELECT {valueSql} AS Value, count() AS Cnt\n" +
            "FROM spans\n" +
            "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            $"WHERE {string.Join(" AND ", whereClauses)}\n" +
            "GROUP BY Value\n" +
            "ORDER BY Cnt DESC, Value\n" +
            "LIMIT {valuesLimit:UInt32}";

        return new ExceptionFacetValuesSql(sql, filterSql.Parameters);
    }
}
