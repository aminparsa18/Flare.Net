using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for the N+1 worst-offenders list.</summary>
public sealed record NPlusOneSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for N+1 query detection (<c>POST /api/traces/n-plus-one</c>): database
/// client spans in one trace that share a parent span and a normalized statement, repeated
/// at least N times. Computed from <c>spans</c> at query time - no new table.
/// </summary>
/// <remarks>
/// The inner query groups by <c>(TraceId, ParentSpanId, ServiceName, Statement)</c> and keeps
/// groups reaching the repeat threshold; the outer one rolls those up per service and
/// statement. The statement is the query text with string and numeric literals replaced by
/// <c>?</c> (parameterized ORM output is already stable; this catches inlined literals), or
/// <c>operation collection</c> when no text was recorded. The same grouping is done client
/// side for the waterfall badge (dashboard <c>traces/n-plus-one.ts</c>) - keep the two in
/// step. Only spans with a parent take part: a root database span has nothing to be a
/// repeat under.
/// </remarks>
public static class NPlusOneQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    public const int DefaultMinRepeats = 10;
    public const int MinMinRepeats = 2;
    public const int MaxMinRepeats = 1000;

    public const int MaxRows = 50;

    /// <summary>Query text, else the older <c>db.statement</c>.</summary>
    private const string RawTextExpr =
        "if(SpanAttributes['db.query.text'] != '', SpanAttributes['db.query.text'], SpanAttributes['db.statement'])";

    /// <summary>The collection/table a statement targets, any semconv generation.</summary>
    private const string CollectionExpr =
        "multiIf(SpanAttributes['db.collection.name'] != '', SpanAttributes['db.collection.name'], " +
        "SpanAttributes['db.sql.table'] != '', SpanAttributes['db.sql.table'], SpanAttributes['db.mongodb.collection'])";

    /// <summary>Normalized statement; empty when the span names neither text nor an operation+collection.</summary>
    public const string StatementExpr =
        $"if({RawTextExpr} != '', " +
        $"replaceRegexpAll(replaceRegexpAll({RawTextExpr}, '''[^'']*''', '?'), '\\\\b[0-9]+\\\\b', '?'), " +
        $"if({ServiceCallBreakdownQueryBuilder.DbOperationExpr} != '' AND {CollectionExpr} != '', " +
        $"concat({ServiceCallBreakdownQueryBuilder.DbOperationExpr}, ' ', {CollectionExpr}), ''))";

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    public static int ClampMinRepeats(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinMinRepeats, MaxMinRepeats) : DefaultMinRepeats;

    /// <summary>
    /// Subquery selecting the ids of traces with an N+1 pattern in the filter window - the
    /// <see cref="SpanFilter.NPlusOneOnly"/> clause. Reads the <c>from</c>/<c>to</c>
    /// parameters <see cref="SpanFilterSqlBuilder"/> already bound; adds <c>n1MinRepeats</c>.
    /// </summary>
    public static string BuildTraceIdQuery(ClickHouseParameterCollection parameters)
    {
        parameters.AddParameter("n1MinRepeats", (ulong)DefaultMinRepeats);
        return "SELECT TraceId FROM spans WHERE " + string.Join(" AND ", BaseClauses()) + "\n" +
            "GROUP BY TraceId, ParentSpanId, " + StatementExpr + "\n" +
            "HAVING " + StatementExpr + " != '' AND count() >= {n1MinRepeats:UInt64}";
    }

    private static List<string> BaseClauses() =>
    [
        "StartTime >= {from:DateTime64(9)}",
        "StartTime < {to:DateTime64(9)}",
        "ParentSpanId != ''",
        $"{ServiceCallBreakdownQueryBuilder.DbSystemExpr} != ''",
    ];

    public static NPlusOneSql Build(NPlusOneRequest request, int windowMinutes, int minRepeats, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);
        parameters.AddParameter("minRepeats", (ulong)minRepeats);
        parameters.AddParameter("limit", (uint)MaxRows);

        var clauses = BaseClauses();

        if (!string.IsNullOrWhiteSpace(request.Service))
        {
            parameters.AddParameter("service", request.Service);
            clauses.Add("ServiceName = {service:String}");
        }

        ServiceScope.Append(clauses, parameters);

        var sql =
            "SELECT\n" +
            "    ServiceName,\n" +
            "    Statement,\n" +
            "    count() AS TraceCount,\n" +
            "    max(Repeats) AS MaxRepeats,\n" +
            "    sum(Repeats) AS TotalRepeats,\n" +
            "    sum(TotalNano) / 1e6 AS TotalDurationMs,\n" +
            "    argMax(TraceId, Repeats) AS ExampleTraceId\n" +
            "FROM (\n" +
            "    SELECT\n" +
            "        TraceId,\n" +
            "        ParentSpanId,\n" +
            "        ServiceName,\n" +
            $"        {StatementExpr} AS Statement,\n" +
            "        count() AS Repeats,\n" +
            "        sum(DurationNano) AS TotalNano\n" +
            "    FROM spans\n" +
            "    WHERE " + string.Join(" AND ", clauses) + "\n" +
            "    GROUP BY TraceId, ParentSpanId, ServiceName, Statement\n" +
            "    HAVING Statement != '' AND Repeats >= {minRepeats:UInt64}\n" +
            ")\n" +
            "GROUP BY ServiceName, Statement\n" +
            "ORDER BY TotalDurationMs DESC\n" +
            "LIMIT {limit:UInt32}";

        return new NPlusOneSql(sql, parameters);
    }
}
