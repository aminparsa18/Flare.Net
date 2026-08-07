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
        if (request.BucketWidthSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.BucketWidthSeconds, "BucketWidthSeconds must be positive.");
        }

        // See LogSearchQueryBuilder's equivalent comment: request.Filter's default
        // doesn't survive JSON deserialization when "filter" is omitted from the body.
        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        filterSql.Parameters.AddParameter("bucketWidth", request.BucketWidthSeconds);

        // GroupBy only ever comes from this closed enum, never request text - safe to
        // interpolate the resulting column name directly, same reasoning as
        // LogFilterSqlBuilder.ColumnFor for the attribute-bag column names.
        var groupColumn = request.GroupBy switch
        {
            LogAggregateGroupBy.Service => "ServiceName",
            LogAggregateGroupBy.Level => "SeverityText",
            _ => null,
        };

        var selectGroup = groupColumn is null ? string.Empty : $"{groupColumn} AS GroupKey, ";
        var groupBy = groupColumn is null ? "BucketStart" : $"BucketStart, {groupColumn}";

        // See LogSearchQueryBuilder's equivalent comment: "{bucketWidth:UInt32}" is a
        // ClickHouse parameter placeholder, kept in a plain (non-interpolated) segment
        // so C# doesn't try to parse it as an interpolation hole.
        var sql = "SELECT toStartOfInterval(Timestamp, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart, " +
            $"{selectGroup}count() AS Count\n" +
            "FROM logs\n" +
            $"WHERE {filterSql.WhereSql}\n" +
            $"GROUP BY {groupBy}\n" +
            "ORDER BY BucketStart";

        return new LogAggregateSql(sql, filterSql.Parameters, HasGroupKey: groupColumn is not null);
    }
}
