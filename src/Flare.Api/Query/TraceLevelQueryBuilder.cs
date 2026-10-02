using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>
/// Pure builder for one depth level of a trace: the direct children of a set of spans, or
/// the trace's root level. <see cref="SpanQueryService"/> walks levels breadth-first to load
/// a trace larger than <see cref="TraceByIdQueryBuilder.MaxSpans"/> lazily instead of
/// truncating it. Same <c>(TraceId, StartTime, SpanId)</c> primary-key prefix as
/// <see cref="TraceByIdQueryBuilder"/>; <c>ParentSpanId</c> is filtered within that one trace.
/// </summary>
public static class TraceLevelQueryBuilder
{
    /// <summary>Spans a lazy load tries to stay under before it stops descending (whole levels only, except the first).</summary>
    public const int LazyBudget = 2_000;

    /// <summary>
    /// The trace's root level: spans with no parent, plus orphans whose parent isn't in the
    /// trace (so a dropped/late parent doesn't make its subtree unreachable, matching
    /// <c>span-tree.ts</c>'s "treat as a root" rule).
    /// </summary>
    public static TraceByIdSql BuildRoots(string traceId)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceId", traceId);
        parameters.AddParameter("limit", (ulong)TraceByIdQueryBuilder.MaxSpans);

        // GLOBAL: in cluster mode both sides read the Distributed table, and a plain NOT IN
        // subquery is denied (same reasoning as SpanFilterSqlBuilder's GLOBAL IN).
        var sql = $"SELECT {SpanColumns.SelectList}\n" +
            "FROM spans\n" +
            "WHERE TraceId = {traceId:String}\n" +
            "  AND (ParentSpanId = '' OR ParentSpanId GLOBAL NOT IN (SELECT SpanId FROM spans WHERE TraceId = {traceId:String}))\n" +
            "ORDER BY StartTime\n" +
            "LIMIT {limit:UInt64}";

        return new TraceByIdSql(sql, parameters);
    }

    /// <summary>Direct children of every span in <paramref name="parentSpanIds"/>.</summary>
    public static TraceByIdSql BuildChildren(string traceId, IReadOnlyCollection<string> parentSpanIds)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceId", traceId);
        parameters.AddParameter("parents", parentSpanIds.ToArray());
        parameters.AddParameter("limit", (ulong)TraceByIdQueryBuilder.MaxSpans);

        var sql = $"SELECT {SpanColumns.SelectList}\n" +
            "FROM spans\n" +
            "WHERE TraceId = {traceId:String}\n" +
            "  AND ParentSpanId IN {parents:Array(String)}\n" +
            "ORDER BY StartTime\n" +
            "LIMIT {limit:UInt64}";

        return new TraceByIdSql(sql, parameters);
    }
}
