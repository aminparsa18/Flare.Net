using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for <c>/api/spans/search</c>, ready to hand to <see cref="SpanQueryService"/>.</summary>
public sealed record SpanSearchSql(string Sql, ClickHouse.Driver.ADO.Parameters.ClickHouseParameterCollection Parameters, int PageSize);

/// <summary>
/// Pure <see cref="SpanSearchRequest"/> → parameterized SQL builder. No ClickHouse
/// dependency - unit-testable on its own, same style as <see cref="LogSearchQueryBuilder"/>.
/// </summary>
/// <remarks>
/// <see cref="SpanSortKey.StartTime"/> and <see cref="SpanSortKey.Duration"/> sort on a
/// stored column. <see cref="SpanSortKey.SpanCount"/> can't - a trace's span count is an
/// aggregate over every span sharing its <c>TraceId</c> - so that sort joins the same
/// <c>GROUP BY TraceId</c> rollup <see cref="SpanRollupQueryBuilder"/> runs, restricted to
/// the traces the filter matches, and appends its <c>SpanCount</c>/<c>HasError</c> as two
/// trailing columns (<see cref="SpanCountSortColumns"/>) the service reads instead of
/// issuing the follow-up rollup query. The rollup is over every matching trace in the
/// window, not one page's worth, so it's the one sort whose cost scales with the
/// window's trace count; the usual execution caps bound it.
/// </remarks>
public static class SpanSearchQueryBuilder
{
    public const int DefaultPageSize = 200;
    public const int MaxPageSize = 1000;

    /// <summary>Extra columns a <see cref="SpanSortKey.SpanCount"/> query selects after <see cref="SpanColumns.SelectList"/>, in order: <c>SpanCount UInt64</c>, <c>HasError UInt8</c>.</summary>
    public const int SpanCountSortColumns = 2;

    public static SpanSearchSql Build(SpanSearchRequest request, DateTimeOffset now, PromotedAttributeColumns? promoted = null)
    {
        // Same System.Text.Json init-only-property caveat LogSearchQueryBuilder guards
        // against - request.Filter's `= new()` default doesn't survive deserialization
        // when the JSON body omits "filter".
        var filterSql = SpanFilterSqlBuilder.Build(request.Filter ?? new SpanFilter(), now, promoted);
        var parameters = filterSql.Parameters;
        var clauses = new List<string> { filterSql.WhereSql };

        var sortBy = Enum.IsDefined(request.SortBy) ? request.SortBy : SpanSortKey.StartTime;
        var ascending = request.SortAscending;
        var sortColumn = sortBy switch
        {
            SpanSortKey.Duration => "DurationNano",
            SpanSortKey.SpanCount => "rollup.SpanCount",
            _ => "StartTime",
        };

        // A cursor minted under another sort/direction (the user re-sorted mid-scroll) is
        // treated as absent - same "malformed = first page" fallback as a tampered one.
        if (SpanSearchCursor.TryDecode(request.Cursor) is { } cursor && cursor.SortBy == sortBy && cursor.Ascending == ascending)
        {
            string cursorValue;
            if (sortBy == SpanSortKey.StartTime)
            {
                parameters.AddParameter("cursorTs", cursor.StartTime.UtcDateTime);
                cursorValue = "{cursorTs:DateTime64(9)}";
            }
            else
            {
                parameters.AddParameter("cursorValue", cursor.Value);
                cursorValue = "{cursorValue:UInt64}";
            }

            parameters.AddParameter("cursorTraceId", cursor.TraceId);
            parameters.AddParameter("cursorSpanId", cursor.SpanId);
            clauses.Add($"({sortColumn}, TraceId, SpanId) {(ascending ? ">" : "<")} ({cursorValue}, {{cursorTraceId:String}}, {{cursorSpanId:String}})");
        }

        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        // Fetch one extra row so SpanQueryService can tell "more pages exist" apart from
        // "this page happened to end exactly at pageSize", same trick as LogSearchQueryBuilder.
        parameters.AddParameter("limit", (uint)(pageSize + 1));

        var direction = ascending ? "ASC" : "DESC";
        string select, from;
        if (sortBy == SpanSortKey.SpanCount)
        {
            parameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");
            // GLOBAL on both the join and the IN so a cluster-mode Distributed `spans`
            // builds each set once rather than per shard - same reasoning as
            // SpanFilterSqlBuilder's entry-span clause; a no-op on a single node.
            select = $"{SpanColumns.SelectList}, rollup.SpanCount, rollup.HasError";
            from = "spans\n" +
                "GLOBAL INNER JOIN\n" +
                "(\n" +
                "    SELECT TraceId, count() AS SpanCount, countIf(StatusCode = {errorStatus:String}) > 0 AS HasError\n" +
                "    FROM spans\n" +
                $"    WHERE TraceId GLOBAL IN (SELECT TraceId FROM spans WHERE {filterSql.WhereSql})\n" +
                "    GROUP BY TraceId\n" +
                ") AS rollup USING (TraceId)";
        }
        else
        {
            select = SpanColumns.SelectList;
            from = "spans";
        }

        var sql = $"SELECT {select}\n" +
            $"FROM {from}\n" +
            $"WHERE {string.Join(" AND ", clauses)}\n" +
            $"ORDER BY {sortColumn} {direction}, TraceId {direction}, SpanId {direction}\n" +
            "LIMIT {limit:UInt64}";

        return new SpanSearchSql(sql, parameters, pageSize);
    }
}
