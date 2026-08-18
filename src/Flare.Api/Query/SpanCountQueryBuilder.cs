using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>TraceId -> span count</c> query plus its bound parameters.</summary>
public sealed record SpanCountSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Builds the follow-up query <see cref="SpanQueryService"/> issues after a
/// <see cref="Model.SpanFilter.RootSpansOnly"/> search, to populate each result row's
/// <see cref="Model.SpanDto.SpanCount"/>. Pure <c>traceIds</c> → parameterized SQL, no
/// ClickHouse dependency - same unit-testable-on-its-own style as
/// <see cref="SpanFilterSqlBuilder"/>.
/// </summary>
/// <remarks>
/// A single <c>GROUP BY</c> over every requested trace id, not one correlated subquery
/// per returned row - cheaper, and <c>TraceId</c> leads <c>spans</c>' <c>ORDER BY</c>
/// (see <c>db/clickhouse/0007_spans.sql</c>'s remarks), so <c>WHERE TraceId IN (...)</c>
/// is a primary-key-prefix lookup, not a scan. Deliberately unbounded by any time range:
/// a trace's non-root spans can start slightly before/after its root span's own
/// timestamp, and undercounting them would defeat the point of the count.
/// </remarks>
public static class SpanCountQueryBuilder
{
    public static SpanCountSql Build(IEnumerable<string> traceIds)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceIds", traceIds.Distinct().ToArray());

        const string sql = "SELECT TraceId, count() AS SpanCount\n" +
            "FROM spans\n" +
            "WHERE TraceId IN {traceIds:Array(String)}\n" +
            "GROUP BY TraceId";

        return new SpanCountSql(sql, parameters);
    }
}
