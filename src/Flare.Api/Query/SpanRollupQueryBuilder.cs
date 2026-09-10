using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>TraceId -> (span count, has-error)</c> query plus its bound parameters.</summary>
public sealed record SpanRollupSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Builds the follow-up query <see cref="SpanQueryService"/> issues after a
/// <see cref="Model.SpanFilter.RootSpansOnly"/> search, to populate each result row's
/// <see cref="Model.SpanDto.SpanCount"/> and <see cref="Model.SpanDto.HasError"/>. Pure
/// <c>traceIds</c> → parameterized SQL, no ClickHouse dependency - same
/// unit-testable-on-its-own style as <see cref="SpanFilterSqlBuilder"/>.
/// </summary>
/// <remarks>
/// A single <c>GROUP BY</c> over every requested trace id, not one correlated subquery
/// per returned row - cheaper, and <c>TraceId</c> leads <c>spans</c>' <c>ORDER BY</c>
/// (see <c>db/clickhouse/0007_spans.sql</c>'s remarks), so <c>WHERE TraceId IN (...)</c>
/// is a primary-key-prefix lookup, not a scan. Deliberately unbounded by any time range:
/// a trace's non-root spans can start slightly before/after its root span's own
/// timestamp, and undercounting (or missing an error on) them would defeat the point of
/// the rollup.
/// <para>
/// <c>HasError</c> rolls up <c>StatusCode</c> across every span sharing the trace id, not
/// just the root - see docs-internal/planning/roadmap.md's now-closed item on trace-list
/// rows showing healthy for a trace whose root span succeeded but a deeper span errored.
/// Computed alongside <c>SpanCount</c> in the same query rather than as a second
/// round-trip, since both are the same <c>GROUP BY TraceId</c> aggregate over the same
/// row set.
/// </para>
/// </remarks>
public static class SpanRollupQueryBuilder
{
    public static SpanRollupSql Build(IEnumerable<string> traceIds)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceIds", traceIds.Distinct().ToArray());
        parameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");

        const string sql = "SELECT TraceId, count() AS SpanCount, countIf(StatusCode = {errorStatus:String}) > 0 AS HasError\n" +
            "FROM spans\n" +
            "WHERE TraceId IN {traceIds:Array(String)}\n" +
            "GROUP BY TraceId";

        return new SpanRollupSql(sql, parameters);
    }
}
