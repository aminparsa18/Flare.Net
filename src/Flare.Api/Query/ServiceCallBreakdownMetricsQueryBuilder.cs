using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized external-calls+database-calls query pair over the pre-aggregated breakdown tables, plus their bound parameters - same shape as <see cref="ServiceCallBreakdownSql"/>.</summary>
public sealed record ServiceCallBreakdownMetricsSql(
    string ExternalCallsSql,
    ClickHouseParameterCollection ExternalCallsParameters,
    string DatabaseCallsSql,
    ClickHouseParameterCollection DatabaseCallsParameters);

/// <summary>
/// Pure (service, window) -&gt; parameterized SQL builder for the Services tab Map
/// view's per-node drill-down, reading the flush-time-pre-aggregated
/// <c>service_call_breakdown_external</c>/<c>service_call_breakdown_database</c>
/// tables (see <c>db/clickhouse/0023_service_dependency_breakdown_metrics.sql</c> and
/// ADR-0031) instead of <see cref="ServiceCallBreakdownQueryBuilder"/>'s live
/// <c>GROUP BY</c>s over <c>spans</c>. Produces the same column shapes
/// (<c>PeerService</c>/<c>CallCount</c>/<c>ErrorCount</c>/<c>P50</c>/<c>P95DurationNano</c>
/// and <c>DbSystem</c>/<c>DbOperation</c>/... respectively) so
/// <see cref="ServiceCallBreakdownQueryService"/> can map either builder's readers the
/// same way.
/// </summary>
/// <remarks>
/// No <see cref="Model.ResourceAttributeFilter"/> parameter, unlike its sibling - same
/// "no dimension for the Services tab's arbitrary filter chips, only called when the
/// caller supplied none" rule as <see cref="ServiceMetricsQueryBuilder"/>.
/// <c>CallCount</c>/<c>ErrorCount</c> are plain <c>sum()</c>s; <c>P50</c>/<c>P95</c>
/// are <c>quantileMerge(p)()</c> over the tables' <c>AggregateFunction(quantile(p),
/// UInt64)</c> state columns - same pattern as <see cref="ServiceMetricsQueryBuilder"/>.
/// Same minute floor/ceil bucket-boundary rounding, copied rather than shared, same
/// precedent as that class.
/// </remarks>
public static class ServiceCallBreakdownMetricsQueryBuilder
{
    public static ServiceCallBreakdownMetricsSql Build(string service, TimeSpan window, DateTimeOffset now)
    {
        var from = FloorToMinute((now - window).UtcDateTime);
        var to = CeilToMinute(now.UtcDateTime);

        var externalCallsParameters = new ClickHouseParameterCollection();
        externalCallsParameters.AddParameter("service", service);
        externalCallsParameters.AddParameter("from", from);
        externalCallsParameters.AddParameter("to", to);

        var externalCallsSql = "SELECT\n" +
            "    PeerService,\n" +
            "    sum(CallCount) AS CallCount,\n" +
            "    sum(ErrorCount) AS ErrorCount,\n" +
            "    quantileMerge(0.5)(P50State) AS P50DurationNano,\n" +
            "    quantileMerge(0.95)(P95State) AS P95DurationNano\n" +
            "FROM service_call_breakdown_external\n" +
            "WHERE ServiceName = {service:String} AND TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}\n" +
            "GROUP BY PeerService\n" +
            "ORDER BY CallCount DESC";

        var databaseCallsParameters = new ClickHouseParameterCollection();
        databaseCallsParameters.AddParameter("service", service);
        databaseCallsParameters.AddParameter("from", from);
        databaseCallsParameters.AddParameter("to", to);

        var databaseCallsSql = "SELECT\n" +
            "    DbSystem,\n" +
            "    DbOperation,\n" +
            "    sum(CallCount) AS CallCount,\n" +
            "    sum(ErrorCount) AS ErrorCount,\n" +
            "    quantileMerge(0.5)(P50State) AS P50DurationNano,\n" +
            "    quantileMerge(0.95)(P95State) AS P95DurationNano\n" +
            "FROM service_call_breakdown_database\n" +
            "WHERE ServiceName = {service:String} AND TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}\n" +
            "GROUP BY DbSystem, DbOperation\n" +
            "ORDER BY CallCount DESC";

        return new ServiceCallBreakdownMetricsSql(externalCallsSql, externalCallsParameters, databaseCallsSql, databaseCallsParameters);
    }

    private static DateTime FloorToMinute(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

    private static DateTime CeilToMinute(DateTime value)
    {
        var floored = FloorToMinute(value);
        return floored == value ? floored : floored.AddMinutes(1);
    }
}
