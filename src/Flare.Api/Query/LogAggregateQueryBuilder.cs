using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>/api/logs/aggregate</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogAggregateSql(string Sql, ClickHouse.Driver.ADO.Parameters.ClickHouseParameterCollection Parameters, bool HasGroupKey);

/// <summary>
/// Pure <see cref="LogAggregateRequest"/> → parameterized SQL builder. No materialized
/// view/pre-aggregation for v1 - see <c>db/clickhouse/README.md</c>'s "No materialized
/// views" note (reviewed against the `clickhouse-architecture-advisor` skill's
/// <c>decision-real-time-preaggregation</c> framework: ad hoc, freshness-first queries
/// belong on the raw table, not behind an incremental MV, until a specific aggregate
/// proves to be a hot repeated path).
/// </summary>
public static class LogAggregateQueryBuilder
{
    public static LogAggregateSql Build(LogAggregateRequest request, DateTimeOffset now)
    {
        // See LogSearchQueryBuilder's equivalent comment: request.Filter's default
        // doesn't survive JSON deserialization when "filter" is omitted from the body.
        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        return BuildFromFilterSql(filterSql, request.BucketWidthSeconds, request.GroupBy);
    }

    /// <summary>
    /// Core builder, split out from <see cref="Build"/> so <c>LogQlQueryBuilder</c> (the
    /// SQL-query-row feature) can reuse the exact same bucketing SQL with its own
    /// already-built <see cref="LogFilterSql"/> (base time bound plus a translated
    /// <c>WHERE</c> fragment from parsed query text) instead of a structured
    /// <see cref="LogFilter"/>. <paramref name="filterSql"/>'s parameter collection is
    /// mutated in place (the bucket-width parameter is added to it), same as <see cref="Build"/>
    /// always did implicitly via <c>filterSql.Parameters</c>.
    /// </summary>
    public static LogAggregateSql BuildFromFilterSql(LogFilterSql filterSql, int bucketWidthSeconds, LogAggregateGroupBy groupBy)
    {
        if (bucketWidthSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketWidthSeconds), bucketWidthSeconds, "BucketWidthSeconds must be positive.");
        }

        filterSql.Parameters.AddParameter("bucketWidth", bucketWidthSeconds);

        // GroupBy only ever comes from this closed enum, never request text - safe to
        // interpolate the resulting column name directly, same reasoning as
        // LogFilterSqlBuilder.ColumnFor for the attribute-bag column names.
        var groupColumn = groupBy switch
        {
            LogAggregateGroupBy.Service => "ServiceName",
            LogAggregateGroupBy.Level => "SeverityText",
            _ => null,
        };

        var selectGroup = groupColumn is null ? string.Empty : $"{groupColumn} AS GroupKey, ";
        var groupByClause = groupColumn is null ? "BucketStart" : $"BucketStart, {groupColumn}";

        // See LogSearchQueryBuilder's equivalent comment: "{bucketWidth:UInt32}" is a
        // ClickHouse parameter placeholder, kept in a plain (non-interpolated) segment
        // so C# doesn't try to parse it as an interpolation hole.
        var sql = "SELECT toStartOfInterval(Timestamp, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart, " +
            $"{selectGroup}count() AS Count\n" +
            "FROM logs\n" +
            $"WHERE {filterSql.WhereSql}\n" +
            $"GROUP BY {groupByClause}\n" +
            "ORDER BY BucketStart";

        return new LogAggregateSql(sql, filterSql.Parameters, HasGroupKey: groupColumn is not null);
    }
}
