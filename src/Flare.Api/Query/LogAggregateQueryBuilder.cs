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
    /// <summary>
    /// How many distinct values a <see cref="LogAggregateGroupBy.Attribute"/> group-by keeps
    /// as their own series - the most frequent ones in the window; the rest roll up into one
    /// null-keyed "other" series. Without a cap, a high-cardinality key (a request id) would
    /// return buckets x distinct-values rows and trip <c>max_result_rows</c>, and a stacked
    /// chart of hundreds of series isn't readable anyway. 5 matches the dashboard's fixed
    /// categorical palette (<c>--chart-1..5</c>), which is never cycled past 5 hues.
    /// </summary>
    public const int AttributeGroupLimit = 5;

    public static LogAggregateSql Build(LogAggregateRequest request, DateTimeOffset now)
    {
        // See LogSearchQueryBuilder's equivalent comment: request.Filter's default
        // doesn't survive JSON deserialization when "filter" is omitted from the body.
        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        return BuildFromFilterSql(
            filterSql,
            request.BucketWidthSeconds,
            request.GroupBy,
            attributeBag: request.GroupByAttributeBag,
            attributeKey: request.GroupByAttributeKey);
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
    public static LogAggregateSql BuildFromFilterSql(
        LogFilterSql filterSql,
        int bucketWidthSeconds,
        LogAggregateGroupBy groupBy,
        string aggregateSql = "count()",
        AttributeBag attributeBag = AttributeBag.Log,
        string? attributeKey = null)
    {
        if (bucketWidthSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketWidthSeconds), bucketWidthSeconds, "BucketWidthSeconds must be positive.");
        }

        filterSql.Parameters.AddParameter("bucketWidth", bucketWidthSeconds);

        if (groupBy == LogAggregateGroupBy.Attribute)
        {
            return BuildAttributeGrouped(filterSql, aggregateSql, attributeBag, attributeKey);
        }

        // GroupBy only ever comes from this closed enum, never request text - safe to
        // interpolate the resulting column name directly, same reasoning as
        // LogFilterSqlBuilder.ColumnFor for the attribute-bag column names.
        var groupColumn = groupBy switch
        {
            LogAggregateGroupBy.Service => "ServiceName",
            LogAggregateGroupBy.Level => "SeverityText",
            LogAggregateGroupBy.Scope => "ScopeName",
            _ => null,
        };

        var selectGroup = groupColumn is null ? string.Empty : $"{groupColumn} AS GroupKey, ";
        var groupByClause = groupColumn is null ? "BucketStart" : $"BucketStart, {groupColumn}";

        // aggregateSql defaults to the literal "count()" every existing caller (the plain
        // Build(request, now) wrapper below - /api/logs/aggregate) relies on, returning a
        // ClickHouse UInt64 column read via GetFieldValue<ulong> in LogQueryService.AggregateAsync
        // - unchanged. LogQlQueryBuilder (the SQL-query-row feature) is the only caller that
        // ever passes something else, and only ever a fixed, closed-set string it builds
        // itself from LogQlAggFunc/LogQlColumn (e.g. "toFloat64(avg(SeverityNumber))") - never
        // request text, same discipline as every other interpolated fragment in this namespace.
        var sql = "SELECT toStartOfInterval(Timestamp, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart, " +
            $"{selectGroup}{aggregateSql} AS Count\n" +
            "FROM logs\n" +
            $"WHERE {filterSql.WhereSql}\n" +
            $"GROUP BY {groupByClause}\n" +
            "ORDER BY BucketStart";

        return new LogAggregateSql(sql, filterSql.Parameters, HasGroupKey: groupColumn is not null);
    }

    /// <summary>
    /// <see cref="LogAggregateGroupBy.Attribute"/> variant: a scalar <c>WITH</c> subquery first
    /// picks the <see cref="AttributeGroupLimit"/> most frequent values of the key under the
    /// same filter, then the bucketed query keeps those values as-is and maps every other
    /// value to NULL (the "other" series). Two scans of the filtered window rather than one,
    /// accepted to keep the result bounded - both are subject to the same execution caps.
    /// A missing key reads as <c>''</c> (ClickHouse's map-subscript default), so events
    /// without it still count - under an empty-string group - rather than silently vanishing
    /// from the stacked total.
    /// </summary>
    private static LogAggregateSql BuildAttributeGrouped(
        LogFilterSql filterSql,
        string aggregateSql,
        AttributeBag attributeBag,
        string? attributeKey)
    {
        if (string.IsNullOrWhiteSpace(attributeKey))
        {
            throw new ArgumentOutOfRangeException(nameof(attributeKey), attributeKey, "GroupByAttributeKey is required when GroupBy is Attribute.");
        }

        filterSql.Parameters.AddParameter("groupByKey", attributeKey);

        // Column name comes from the closed AttributeBag enum (LogFilterSqlBuilder.ColumnFor),
        // the key is a bound parameter - nothing from request text is interpolated.
        var valueSql = $"{LogFilterSqlBuilder.ColumnFor(attributeBag)}[{{groupByKey:String}}]";

        var sql = "WITH (\n" +
            "    SELECT groupArray(GroupValue) FROM (\n" +
            $"        SELECT {valueSql} AS GroupValue\n" +
            "        FROM logs\n" +
            $"        WHERE {filterSql.WhereSql}\n" +
            "        GROUP BY GroupValue\n" +
            "        ORDER BY count() DESC\n" +
            $"        LIMIT {AttributeGroupLimit}\n" +
            "    )\n" +
            ") AS TopGroupValues\n" +
            "SELECT toStartOfInterval(Timestamp, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart, " +
            $"if(has(TopGroupValues, {valueSql}), {valueSql}, NULL) AS GroupKey, " +
            $"{aggregateSql} AS Count\n" +
            "FROM logs\n" +
            $"WHERE {filterSql.WhereSql}\n" +
            "GROUP BY BucketStart, GroupKey\n" +
            "ORDER BY BucketStart";

        return new LogAggregateSql(sql, filterSql.Parameters, HasGroupKey: true);
    }
}
