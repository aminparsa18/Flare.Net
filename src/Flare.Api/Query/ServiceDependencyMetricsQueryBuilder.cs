using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized Map-view nodes rollup query over the pre-aggregated <c>service_dependency_nodes</c> table, plus its bound parameters.</summary>
public sealed record ServiceDependencyMetricsSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure window -&gt; parameterized SQL builder for the Traces page's Services tab Map
/// view's <b>nodes</b>, reading the flush-time-pre-aggregated
/// <c>service_dependency_nodes</c> table (see
/// <c>db/clickhouse/0023_service_dependency_breakdown_metrics.sql</c> and ADR-0031)
/// instead of <see cref="ServiceDependencyQueryBuilder"/>'s live nodes
/// <c>GROUP BY</c> over <c>spans</c>. Produces the same 5-column result shape
/// (<c>Service</c>, <c>SpanCount</c>, <c>ErrorCount</c>, <c>TotalDurationNano</c>,
/// <c>TopOperations</c>) so <see cref="ServiceDependencyQueryService"/> can map either
/// builder's reader the same way. Has no edges counterpart - see ADR-0031's Context
/// for why edges remain a live-only query.
/// </summary>
/// <remarks>
/// No <see cref="Model.ResourceAttributeFilter"/> parameter, unlike its sibling - same
/// "no dimension for the Services tab's arbitrary filter chips, only called when the
/// caller supplied none" rule as <see cref="ServiceMetricsQueryBuilder"/>.
/// <para>
/// <c>SpanCount</c>/<c>ErrorCount</c>/<c>TotalDurationNano</c> are plain <c>sum()</c>s
/// over <c>SimpleAggregateFunction(sum, UInt64)</c> columns. <c>TopOperations</c> is
/// NOT a plain aggregate - <c>topK</c> sketches merge via ClickHouse's own
/// <c>topKMerge</c>, the same "state blob in, real value out via a Merge combinator"
/// pattern <see cref="ServiceMetricsQueryBuilder"/> already uses for percentiles.
/// </para>
/// <para>
/// Same minute floor/ceil bucket-boundary rounding as
/// <see cref="ServiceMetricsQueryBuilder"/> - copied rather than shared, matching that
/// class's own precedent.
/// </para>
/// </remarks>
public static class ServiceDependencyMetricsQueryBuilder
{
    public static ServiceDependencyMetricsSql Build(TimeSpan window, DateTimeOffset now)
    {
        var from = FloorToMinute((now - window).UtcDateTime);
        var to = CeilToMinute(now.UtcDateTime);

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", from);
        parameters.AddParameter("to", to);

        var sql = "SELECT\n" +
            "    Service,\n" +
            "    sum(SpanCount) AS SpanCount,\n" +
            "    sum(ErrorCount) AS ErrorCount,\n" +
            "    sum(TotalDurationNano) AS TotalDurationNano,\n" +
            "    topKMerge(3)(TopOperationsState) AS TopOperations\n" +
            "FROM service_dependency_nodes\n" +
            "WHERE TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}\n" +
            "GROUP BY Service\n" +
            "ORDER BY SpanCount DESC";

        return new ServiceDependencyMetricsSql(sql, parameters);
    }

    private static DateTime FloorToMinute(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

    private static DateTime CeilToMinute(DateTime value)
    {
        var floored = FloorToMinute(value);
        return floored == value ? floored : floored.AddMinutes(1);
    }
}
