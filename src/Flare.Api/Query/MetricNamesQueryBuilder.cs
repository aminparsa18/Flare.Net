using ClickHouse.Driver.ADO.Parameters;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for <c>POST /api/metrics/names</c>, ready to hand to <see cref="MetricQueryService"/>.</summary>
public sealed record MetricNamesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="MetricNamesRequest"/> → parameterized SQL builder for metric
/// discovery: every distinct metric name in scope, across all three point-type tables,
/// tagged with which table (point type) it came from. No ClickHouse dependency, same
/// "pure function" style as the other builders.
/// </summary>
/// <remarks>
/// A <c>UNION ALL</c> of one <c>GROUP BY MetricName</c> query per table - there's no
/// single table to query, by construction (see <c>db/clickhouse/0008_metrics.sql</c>'s
/// "three tables, not one" design decision). The same <see cref="MetricFilterSqlBuilder"/>
/// output is reused verbatim in all three branches (and its parameters bound once, not
/// duplicated per branch) - it's schema-identical across the three tables' shared
/// <c>Time</c>/<c>ServiceName</c>/<c>DataPointAttributes</c> columns.
///
/// The three branches are wrapped in a subquery before the final <c>ORDER BY</c> is
/// applied - confirmed live against ClickHouse (not assumed) that a trailing
/// <c>ORDER BY</c> directly after a chain of <c>UNION ALL</c> SELECTs binds only to the
/// last branch, not the combined result, leaving the other branches in arbitrary order.
/// </remarks>
public static class MetricNamesQueryBuilder
{
    public static MetricNamesSql Build(MetricNamesRequest request, DateTimeOffset now)
    {
        var filter = new MetricFilter { From = request.From, To = request.To, Services = request.Services };
        var filterSql = MetricFilterSqlBuilder.Build(filter, now);

        // Grouped by (MetricName, ServiceName), not MetricName alone: the same metric
        // name can legitimately be emitted by more than one service, and collapsing
        // that away here would make it impossible for the picker to tell them apart -
        // the same reason MetricSeriesQueryBuilder groups series by ServiceName too
        // (see its remarks).
        var sql = "SELECT * FROM (\n" +
            $"SELECT MetricName, ServiceName, 'gauge' AS Type, any(Unit) AS Unit, any(Description) AS Description FROM metrics_gauge WHERE {filterSql.WhereSql} GROUP BY MetricName, ServiceName\n" +
            "UNION ALL\n" +
            $"SELECT MetricName, ServiceName, 'sum' AS Type, any(Unit) AS Unit, any(Description) AS Description FROM metrics_sum WHERE {filterSql.WhereSql} GROUP BY MetricName, ServiceName\n" +
            "UNION ALL\n" +
            $"SELECT MetricName, ServiceName, 'histogram' AS Type, any(Unit) AS Unit, any(Description) AS Description FROM metrics_histogram WHERE {filterSql.WhereSql} GROUP BY MetricName, ServiceName\n" +
            ")\n" +
            "ORDER BY MetricName, ServiceName";

        return new MetricNamesSql(sql, filterSql.Parameters);
    }
}
