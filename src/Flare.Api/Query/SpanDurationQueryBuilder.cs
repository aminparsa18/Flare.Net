using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>(TraceId, SpanId) -&gt; DurationNano</c> query plus its bound parameters.</summary>
public sealed record SpanDurationSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Builds the follow-up query <see cref="LogQueryService"/> issues after a
/// <see cref="Model.LogSearchRequest.IncludeSpanDuration"/> search, to populate each
/// result row's <see cref="Model.LogEventDto.SpanDurationNano"/>. Pure
/// <c>(TraceId, SpanId)</c> pairs -&gt; parameterized SQL, no ClickHouse dependency - same
/// unit-testable-on-its-own style as <see cref="SpanCountQueryBuilder"/>.
/// </summary>
/// <remarks>
/// Deliberately <c>TraceId IN (...) AND SpanId IN (...)</c> over two flat string
/// arrays, not a single <c>WHERE (TraceId, SpanId) IN {pairs:Array(Tuple(String,String))}</c>
/// clause - no existing usage of an <c>Array(Tuple(...))</c> parameter exists anywhere in
/// this codebase to confirm <c>ClickHouse.Driver</c> 1.3.0 actually supports binding one,
/// and this item shouldn't block on that uncertainty. This looser filter can, in
/// principle, also match a spurious cross-trace <c>(TraceId, SpanId)</c> combination
/// that was never actually requested (e.g. trace A's SpanId happening to also appear
/// under trace B) - astronomically unlikely under OTel's 8-byte random SpanId, but the
/// caller (<see cref="LogQueryService"/>'s follow-up merge) enforces exact-pair
/// correctness anyway by keying its merge dictionary on the full (TraceId, SpanId) pair,
/// so a spurious row is simply never looked up. Cheap regardless: <c>TraceId</c> leads
/// <c>spans</c>' <c>ORDER BY</c> (see <c>db/clickhouse/0007_spans.sql</c>'s remarks), so
/// <c>WHERE TraceId IN (...)</c> alone is already a primary-key-prefix lookup, not a
/// scan - the SpanId filter narrows further but isn't what makes this cheap.
/// Deliberately unbounded by any time range, same rationale as
/// <see cref="SpanCountQueryBuilder"/>: bounded already by the caller only ever passing
/// one page's worth of distinct pairs.
/// </remarks>
public static class SpanDurationQueryBuilder
{
    public static SpanDurationSql Build(IEnumerable<(string TraceId, string SpanId)> pairs)
    {
        var distinctPairs = pairs.Distinct().ToList();

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceIds", distinctPairs.Select(p => p.TraceId).Distinct().ToArray());
        parameters.AddParameter("spanIds", distinctPairs.Select(p => p.SpanId).Distinct().ToArray());

        const string sql = "SELECT TraceId, SpanId, DurationNano\n" +
            "FROM spans\n" +
            "WHERE TraceId IN {traceIds:Array(String)} AND SpanId IN {spanIds:Array(String)}";

        return new SpanDurationSql(sql, parameters);
    }
}
