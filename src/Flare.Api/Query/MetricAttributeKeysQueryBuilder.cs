using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/metrics/attribute-keys</c>, ready to hand to <see cref="MetricQueryService"/>.</summary>
public sealed record MetricAttributeKeysSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="MetricAttributeKeysRequest"/> → parameterized SQL builder: every
/// distinct <c>DataPointAttributes</c> key present on one metric in scope, each with a
/// distinct-value count. Populates the "Group by" picker's option list -
/// <see cref="MetricSeriesQueryBuilder.Build"/> is the query that actually groups by a
/// chosen one of these keys.
/// </summary>
/// <remarks>
/// <para>
/// Genuinely new query shape - unlike <see cref="MetricNamesQueryBuilder"/>'s
/// <c>count(DISTINCT toString(DataPointAttributes)) AS SeriesCount</c> (which counts
/// distinct whole-map combinations without ever needing to enumerate individual keys),
/// this has to expand each row into one row per key it carries
/// (<c>arrayJoin(mapKeys(DataPointAttributes))</c>) before it can group by key. The
/// existing <c>idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter</c> skip
/// index (see <c>db/clickhouse/0008_metrics.sql</c>) is for equality-filter pruning, not
/// enumeration - this still does a full scan of <c>DataPointAttributes</c> over the
/// <c>MetricName</c>-filtered rows, the same cost class as
/// <see cref="MetricNamesQueryBuilder"/>'s own <c>count(DISTINCT ...)</c>.
/// </para>
/// <para>
/// Scoped by <see cref="Model.MetricAttributeKeysRequest.Type"/> to a single table (via
/// <see cref="MetricTables"/>), not a three-table <c>UNION ALL</c> like
/// <see cref="MetricNamesQueryBuilder"/> - the caller already knows the metric's type
/// from a prior discovery response, same convention <see cref="MetricSeriesQueryBuilder"/>
/// already uses.
/// </para>
/// </remarks>
public static class MetricAttributeKeysQueryBuilder
{
    public static MetricAttributeKeysSql Build(MetricAttributeKeysRequest request, DateTimeOffset now)
    {
        var filterSql = MetricFilterSqlBuilder.Build(request.Filter ?? new MetricFilter(), now);
        filterSql.Parameters.AddParameter("metricName", request.MetricName);

        var table = MetricTables.For(request.Type);

        var sql = "SELECT Key, count(DISTINCT DataPointAttributes[Key]) AS DistinctValueCount\n" +
            "FROM (\n" +
            "    SELECT DataPointAttributes, arrayJoin(mapKeys(DataPointAttributes)) AS Key\n" +
            $"    FROM {table}\n" +
            $"    WHERE MetricName = {{metricName:String}} AND {filterSql.WhereSql}\n" +
            ")\n" +
            "GROUP BY Key\n" +
            "ORDER BY Key";

        return new MetricAttributeKeysSql(sql, filterSql.Parameters);
    }
}
