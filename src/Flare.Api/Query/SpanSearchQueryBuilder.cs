using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for <c>/api/spans/search</c>, ready to hand to <see cref="SpanQueryService"/>.</summary>
public sealed record SpanSearchSql(string Sql, ClickHouse.Driver.ADO.Parameters.ClickHouseParameterCollection Parameters, int PageSize);

/// <summary>
/// Pure <see cref="SpanSearchRequest"/> → parameterized SQL builder. No ClickHouse
/// dependency - unit-testable on its own, same style as <see cref="LogSearchQueryBuilder"/>.
/// </summary>
public static class SpanSearchQueryBuilder
{
    public const int DefaultPageSize = 200;
    public const int MaxPageSize = 1000;

    public static SpanSearchSql Build(SpanSearchRequest request, DateTimeOffset now)
    {
        // Same System.Text.Json init-only-property caveat LogSearchQueryBuilder guards
        // against - request.Filter's `= new()` default doesn't survive deserialization
        // when the JSON body omits "filter".
        var filterSql = SpanFilterSqlBuilder.Build(request.Filter ?? new SpanFilter(), now);
        var clauses = new List<string> { filterSql.WhereSql };

        if (SpanSearchCursor.TryDecode(request.Cursor) is { } cursor)
        {
            filterSql.Parameters.AddParameter("cursorTs", cursor.StartTime.UtcDateTime);
            filterSql.Parameters.AddParameter("cursorTraceId", cursor.TraceId);
            filterSql.Parameters.AddParameter("cursorSpanId", cursor.SpanId);
            clauses.Add("(StartTime, TraceId, SpanId) < ({cursorTs:DateTime64(9)}, {cursorTraceId:String}, {cursorSpanId:String})");
        }

        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        // Fetch one extra row so SpanQueryService can tell "more pages exist" apart from
        // "this page happened to end exactly at pageSize", same trick as LogSearchQueryBuilder.
        filterSql.Parameters.AddParameter("limit", (uint)(pageSize + 1));

        var sql = $"SELECT {SpanColumns.SelectList}\n" +
            "FROM spans\n" +
            $"WHERE {string.Join(" AND ", clauses)}\n" +
            "ORDER BY StartTime DESC, TraceId DESC, SpanId DESC\n" +
            "LIMIT {limit:UInt64}";

        return new SpanSearchSql(sql, filterSql.Parameters, pageSize);
    }
}
