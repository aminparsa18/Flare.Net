using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized per-service RED-metrics aggregate query plus its bound parameters.</summary>
public sealed record ServiceOverviewSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure window → parameterized SQL builder for the Services landing page's per-service
/// Rate/Errors/Duration rollup - one <c>GROUP BY ServiceName</c> over <c>spans</c>. Same
/// "pure function, no ClickHouse dependency" style as <see cref="ActiveServicesQueryBuilder"/>/
/// <see cref="SpanCountQueryBuilder"/>, split out of <see cref="ServiceOverviewQueryService"/>
/// so the SQL shape is unit-testable on its own.
/// </summary>
/// <remarks>
/// Counts root spans only (<c>ParentSpanId = ''</c>) as a service's "requests" - the same
/// entry-point convention <see cref="Model.SpanFilter.RootSpansOnly"/> already establishes
/// for the trace-list view (<see cref="SpanEndpoints"/>'s remarks), read the other way:
/// a service's root spans are (in the overwhelming common case) exactly its inbound
/// request entry points, so reusing that convention here keeps "requests" meaning the
/// same thing everywhere this codebase counts them, rather than inventing a second,
/// SpanKind-based definition (SERVER/CONSUMER) that would disagree with it for any trace
/// whose root happens to be a CLIENT/INTERNAL span under partial instrumentation.
/// <para>
/// No index on <c>ServiceName</c> helps an unfiltered <c>GROUP BY</c> like this one - the
/// bloom filter skip index only prunes granules for an *equality* filter, and there isn't
/// one here by design (every service is wanted). This is therefore a bounded-by-partition
/// scan of the window's <c>StartTime</c> range, same accepted tradeoff
/// <see cref="ActiveServicesQueryBuilder"/>'s own remarks document for the analogous
/// unfiltered-by-service query over <c>logs</c>.
/// </para>
/// </remarks>
public static class ServiceOverviewQueryBuilder
{
    public const int DefaultWindowMinutes = 15;
    public const int MinWindowMinutes = 1;
    public const int MaxWindowMinutes = 1440;

    /// <summary>Clamps a caller-supplied window to a sane range, defaulting a non-positive value to <see cref="DefaultWindowMinutes"/> - same shape as <c>IngestionStatsQueryService.ClampMinutes</c>.</summary>
    public static int ClampWindowMinutes(int requested) =>
        Math.Clamp(requested <= 0 ? DefaultWindowMinutes : requested, MinWindowMinutes, MaxWindowMinutes);

    public static ServiceOverviewSql Build(TimeSpan window, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", (now - window).UtcDateTime);
        parameters.AddParameter("to", now.UtcDateTime);
        parameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");

        const string sql = "SELECT\n" +
            "    ServiceName,\n" +
            "    count() AS RequestCount,\n" +
            "    countIf(StatusCode = {errorStatus:String}) AS ErrorCount,\n" +
            "    quantile(0.5)(DurationNano) AS P50DurationNano,\n" +
            "    quantile(0.95)(DurationNano) AS P95DurationNano,\n" +
            "    quantile(0.99)(DurationNano) AS P99DurationNano\n" +
            "FROM spans\n" +
            "WHERE StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)} AND ParentSpanId = ''\n" +
            "GROUP BY ServiceName\n" +
            "ORDER BY RequestCount DESC";

        return new ServiceOverviewSql(sql, parameters);
    }
}
