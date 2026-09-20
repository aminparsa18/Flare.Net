using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>A parameterized per-service RED-metrics rollup query over the pre-aggregated <c>service_metrics</c> table, plus its bound parameters.</summary>
public sealed record ServiceMetricsSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure window -&gt; parameterized SQL builder for the Traces page's Services tab Table
/// view, reading the flush-time-pre-aggregated <c>service_metrics</c> table (see
/// <c>db/clickhouse/0022_service_metrics.sql</c> and ADR-0030) instead of
/// <see cref="ServiceOverviewQueryBuilder"/>'s live <c>GROUP BY</c> over <c>spans</c>.
/// Produces the exact same 6-column result shape (<c>ServiceName</c>, <c>RequestCount</c>,
/// <c>ErrorCount</c>, <c>P50/P95/P99DurationNano</c>) so <see cref="ServiceOverviewQueryService"/>
/// can feed either builder's result through the same <c>BuildMetrics</c> mapper.
/// </summary>
/// <remarks>
/// No <see cref="Model.ResourceAttributeFilter"/> parameter, unlike its sibling - the
/// Services tab's filter chips are arbitrary free-text key/value pairs, and
/// <c>service_metrics</c> has no dimension for them (only <c>ServiceName</c> +
/// one-minute <c>TimeBucket</c>). <see cref="ServiceOverviewQueryService"/> only calls this
/// builder when the caller supplied no resource-attribute filters at all; any filter
/// present falls back to <see cref="ServiceOverviewQueryBuilder"/>'s live path instead.
/// <para>
/// <c>RequestCount</c>/<c>ErrorCount</c> are plain <c>sum()</c>s over the table's
/// <c>SimpleAggregateFunction(sum, UInt64)</c> columns - trivially mergeable across the
/// one-minute buckets a window spans. The percentile columns are NOT plain sums -
/// percentiles don't merge that way - so they're <c>AggregateFunction(quantile(p),
/// UInt64)</c> state blobs, combined back into a real percentile via
/// <c>quantileMerge(p)()</c>, ClickHouse's standard mechanism for merging partial quantile
/// state across rows (here: across buckets, and in cluster mode, across shards).
/// </para>
/// <para>
/// <b>Bucket-boundary rounding</b>: <c>TimeBucket</c> is minute-truncated
/// (<c>toStartOfMinute</c>), but a caller's <c>from</c>/<c>to</c> generally aren't. Binding
/// the raw, non-aligned instants against <c>TimeBucket &gt;=/&lt;</c> would silently *drop*
/// up to a minute of genuinely in-window data at the start of the range (a bucket whose
/// start instant is technically before <c>from</c> even though some of the spans that rolled
/// into it happened after). Instead <paramref name="window"/>'s bounds are floored (start)
/// and ceilinged (end) to whole minutes before binding, so the query can only ever
/// *over*-include up to roughly a minute of data at each edge - a bounded, documented
/// approximation (see ADR-0030's Consequences section), never a silently-dropped one. This
/// mirrors this codebase's existing approximate-aggregate precedents (<c>quantile()</c>
/// itself, <c>topK()</c> in <see cref="ServiceDependencyQueryBuilder"/>) in spirit: an
/// accepted, named tradeoff rather than an unexamined one.
/// </para>
/// </remarks>
public static class ServiceMetricsQueryBuilder
{
    public static ServiceMetricsSql Build(TimeSpan window, DateTimeOffset now)
    {
        var from = FloorToMinute((now - window).UtcDateTime);
        var to = CeilToMinute(now.UtcDateTime);

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", from);
        parameters.AddParameter("to", to);

        var sql = "SELECT\n" +
            "    ServiceName,\n" +
            "    sum(RequestCount) AS RequestCount,\n" +
            "    sum(ErrorCount) AS ErrorCount,\n" +
            "    quantileMerge(0.5)(P50State) AS P50DurationNano,\n" +
            "    quantileMerge(0.95)(P95State) AS P95DurationNano,\n" +
            "    quantileMerge(0.99)(P99State) AS P99DurationNano\n" +
            "FROM service_metrics\n" +
            "WHERE TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}\n" +
            "GROUP BY ServiceName\n" +
            "ORDER BY RequestCount DESC";

        return new ServiceMetricsSql(sql, parameters);
    }

    private static DateTime FloorToMinute(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

    private static DateTime CeilToMinute(DateTime value)
    {
        var floored = FloorToMinute(value);
        return floored == value ? floored : floored.AddMinutes(1);
    }
}
