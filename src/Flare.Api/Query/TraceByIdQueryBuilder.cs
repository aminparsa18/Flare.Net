using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for <c>GET /api/traces/{traceId}</c>, ready to hand to <see cref="SpanQueryService"/>.</summary>
public sealed record TraceByIdSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <c>traceId</c> → parameterized SQL builder for the waterfall's one query: every
/// span sharing a <c>TraceId</c>, ordered so a trace renders top-to-bottom. No
/// ClickHouse dependency, same "pure function" style as the other builders.
/// </summary>
/// <remarks>
/// Unlike <see cref="SpanSearchQueryBuilder"/>, this hits the <c>spans</c> table's
/// <c>ORDER BY (TraceId, StartTime, SpanId)</c> prefix directly (see
/// <c>db/clickhouse/0007_spans.sql</c>'s remarks on why that ordering was chosen) - no
/// keyset pagination needed, just a safety <see cref="MaxSpans"/> cap in case a
/// pathological trace has an unbounded number of spans, mirroring
/// <see cref="LogQueryService"/>'s <c>max_result_rows</c> safety setting rather than
/// trusting the caller or the data to stay small.
/// </remarks>
public static class TraceByIdQueryBuilder
{
    public const uint MaxSpans = 10_000;

    public static TraceByIdSql Build(string traceId)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceId", traceId);
        parameters.AddParameter("limit", MaxSpans);

        var sql = $"SELECT {SpanColumns.SelectList}\n" +
            "FROM spans\n" +
            "WHERE TraceId = {traceId:String}\n" +
            "ORDER BY StartTime\n" +
            "LIMIT {limit:UInt64}";

        return new TraceByIdSql(sql, parameters);
    }
}
