using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/metrics/query</c>, ready to hand to <see cref="MetricQueryService"/>.</summary>
public sealed record MetricSeriesSql(string Sql, ClickHouseParameterCollection Parameters, MetricPointType Type);

/// <summary>
/// Pure <see cref="MetricQueryRequest"/> → parameterized SQL builder: one time-bucketed
/// series per distinct (<c>ServiceName</c>, <c>DataPointAttributes</c>) pair. No
/// materialized view/pre-aggregation for v1 - same "ad hoc, raw-table" call as
/// <see cref="LogAggregateQueryBuilder"/>, and for the same reason (freshness-first,
/// filters vary per dashboard interaction, no fixed repeated aggregate yet).
/// </summary>
/// <remarks>
/// <para>
/// <c>ServiceName</c> is its own <c>GROUP BY</c> column, not folded into the series key
/// or left out: the same metric name can be emitted by more than one service (see
/// <see cref="MetricNamesQueryBuilder"/>'s remarks for the same point applied to
/// discovery), and a request with no (or a multi-value) <see cref="Model.MetricFilter.Services"/>
/// filter can genuinely span more than one. Without grouping by it, two services'
/// samples sharing the same (or no) <c>DataPointAttributes</c> would silently merge into
/// one line - a real correctness bug caught before this ever reached the dashboard, not
/// just a cosmetic gap.
/// </para>
/// <para>
/// <c>toString(DataPointAttributes)</c>, not the <c>Map</c> column itself, is the
/// attribute half of the series key - sidesteps relying on <c>Map</c>-column grouping/
/// hashing semantics (unconfirmed against this ClickHouse version at build time) for
/// something a plain string equality trivially guarantees instead; the real map is still
/// selected back via <c>any(DataPointAttributes)</c> for the response. This is the
/// <b>ungrouped</b> shape - see the next paragraph for <see cref="Model.MetricQueryRequest.GroupByAttributeKey"/>.
/// </para>
/// <para>
/// <b>Grouped mode</b> (<see cref="Model.MetricQueryRequest.GroupByAttributeKey"/> set):
/// <c>SeriesKey</c> becomes <c>DataPointAttributes[key]</c> - the one chosen key's value -
/// instead of the whole serialized map, so every row sharing that value collapses into
/// one series regardless of what else differs on <c>DataPointAttributes</c>.
/// <c>SeriesAttributes</c> becomes a synthetic single-key map
/// (<c>map(key, any(DataPointAttributes[key]))</c>) built in SQL rather than the full
/// map, so it stays <c>Map</c>-shaped in both modes and <see cref="MetricQueryService"/>'s
/// ordinal-3 fold logic needs no branch for which mode produced a given response. Map
/// subscript access on a missing key returns the value type's default (empty string), not
/// an error - already relied on by <see cref="MetricFilterSqlBuilder"/>'s own
/// attribute-equality filter - so a data point missing the chosen key and one with a
/// genuinely empty value for it both collapse into one <c>"(none)"</c>-rendering series
/// (the dashboard's <c>compactSeriesLabel</c> already has that fallback). Deliberate, not
/// disambiguated via <c>mapContains</c> - confirmed as the intended v1 behavior, same
/// "named, deliberately-unresolved" tradeoff this file's Sum/Histogram remarks already
/// make elsewhere. The per-type <c>valueSelect</c> expressions below are unchanged by
/// grouping mode - they're true aggregates that keep meaning the same thing over the
/// wider, coarser groups grouped mode produces.
/// </para>
/// <para>
/// <b>Gauge:</b> <c>avg(Value)</c> per bucket - the "current value" point type has no
/// aggregation temporality to reason about (see the OTLP spec's own note that Gauge
/// ignores <c>StartTimeUnixNano</c>), so averaging within a bucket is the natural
/// downsample.
/// </para>
/// <para>
/// <b>Sum:</b> <c>max(Value) - min(Value)</c> per bucket, plus <c>count()</c> - the raw
/// number of <c>metrics_sum</c> rows folded into the bucket, exposed as
/// <see cref="Model.MetricSeriesPoint.Count"/> for the chart's "Count" aggregation-mode
/// option. The <c>max - min</c> delta is the v1 approximation documented in Planning.md's
/// v6 as a named, deliberately-unresolved limitation: correct for a monotonic, cumulative
/// counter with no resets inside the bucket (the overwhelmingly common .NET case -
/// ASP.NET Core/System.Runtime instrumentation), but a process restart or counter reset
/// mid-bucket will read as a dip rather than the true increase. Not branched on
/// <c>AggregationTemporality</c>/<c>IsMonotonic</c> in v1 - doing that correctly (delta
/// sums want <c>sum(Value)</c>, not <c>max - min</c>) is a real, separate piece of work
/// flagged for a later pass, not silently half-solved here.
/// </para>
/// <para>
/// <b>Histogram:</b> <c>sum(Count)</c>/<c>sum(Sum)</c> per bucket, plus
/// <c>sumForEach(BucketCounts)</c> - the aggregate-combinator that sums arrays
/// element-wise across the grouped rows (requires every row in a group to share the same
/// bucket layout, which holds for one metric name's histogram in practice) - and
/// <c>any(ExplicitBounds)</c> (assumed stable across the group, same assumption the
/// combinator itself makes). <see cref="MetricQueryService"/> feeds the resulting
/// per-bucket arrays through <see cref="HistogramQuantileEstimator"/> to derive
/// p50/p75/p90/p95/p99 and an approximate max - the second named, deliberately-unresolved
/// v1 limitation (assumes bucket boundaries don't change mid-window).
/// </para>
/// <para>
/// <b>Series cap</b> (<see cref="Model.MetricQueryRequest.TopN"/>): a high-cardinality
/// <see cref="Model.MetricQueryRequest.GroupByAttributeKey"/> (or even an ungrouped query
/// over a metric with many distinct <c>DataPointAttributes</c> maps) can otherwise produce
/// thousands of series, unlike every other ClickHouse query in this codebase which
/// deliberately caps result size. Applied unconditionally (clamped between
/// <see cref="DefaultTopN"/> and <see cref="MaxTopN"/> via <see cref="RangeSql"/> below),
/// same "always on, ranked list" convention as <see cref="ExceptionGroupQueryBuilder"/>/
/// <see cref="LogPatternQueryBuilder"/> - except a bucketed series can't just <c>LIMIT</c>
/// the flat row set (that would cut a series off mid-window, not drop it entirely), so the
/// cap is a subquery: rank distinct (<c>ServiceName</c>, <c>SeriesKey</c>) pairs by the
/// same per-type magnitude the main query's <c>valueSelect</c> already computes - summed/
/// averaged/counted over the <em>whole</em> requested window, not per-bucket - then
/// restrict the bucketed query to that top-N set via a tuple <c>IN</c>. This also answers
/// the UX half of the same roadmap item ("top 10 hosts by error rate"): series are ranked
/// by magnitude, not returned in arbitrary (alphabetical) order and truncated.
/// </para>
/// </remarks>
public static class MetricSeriesQueryBuilder
{
    /// <summary>Same shape of "ranked, bounded aggregate list" default as <see cref="ExceptionGroupQueryBuilder.DefaultTopN"/>/<see cref="LogPatternQueryBuilder.DefaultTopN"/>, just smaller - these are chart lines, not table rows.</summary>
    public const int DefaultTopN = 20;

    private const int MaxTopN = 200;

    public static MetricSeriesSql Build(MetricQueryRequest request, DateTimeOffset now)
    {
        if (request.BucketWidthSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.BucketWidthSeconds, "BucketWidthSeconds must be positive.");
        }

        // Same System.Text.Json init-only-property caveat LogSearchQueryBuilder guards
        // against - request.Filter's `= new()` default doesn't survive deserialization
        // when the JSON body omits "filter".
        var filterSql = MetricFilterSqlBuilder.Build(request.Filter ?? new MetricFilter(), now);
        filterSql.Parameters.AddParameter("metricName", request.MetricName);
        filterSql.Parameters.AddParameter("bucketWidth", request.BucketWidthSeconds);

        var topN = Math.Clamp(request.TopN is > 0 ? request.TopN.Value : DefaultTopN, 1, MaxTopN);
        filterSql.Parameters.AddParameter("topN", (uint)topN);

        var table = MetricTables.For(request.Type);
        var valueSelect = request.Type switch
        {
            MetricPointType.Gauge => "avg(Value) AS Value",
            MetricPointType.Sum => "max(Value) - min(Value) AS Value, count() AS Count",
            MetricPointType.Histogram => "sum(Count) AS Count, sum(Sum) AS SumTotal, sumForEach(BucketCounts) AS BucketCounts, any(ExplicitBounds) AS ExplicitBounds",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unknown metric point type."),
        };

        // Same per-type aggregate valueSelect uses, minus the alias - the magnitude a
        // series is ranked by for the top-N cap above. Deliberately the whole-window
        // aggregate (no BucketStart in the ranking subquery's GROUP BY), not a per-bucket
        // one: "top 10 hosts" means top over the requested range, not top-in-the-first-bucket.
        var rankExpr = request.Type switch
        {
            MetricPointType.Gauge => "avg(Value)",
            MetricPointType.Sum => "max(Value) - min(Value)",
            MetricPointType.Histogram => "sum(Count)",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unknown metric point type."),
        };

        string seriesKeyExpr;
        string rawSeriesKeyExpr;
        string seriesAttributesExpr;
        if (string.IsNullOrEmpty(request.GroupByAttributeKey))
        {
            rawSeriesKeyExpr = "toString(DataPointAttributes)";
            seriesKeyExpr = $"{rawSeriesKeyExpr} AS SeriesKey";
            seriesAttributesExpr = "any(DataPointAttributes) AS SeriesAttributes";
        }
        else
        {
            filterSql.Parameters.AddParameter("groupByKey", request.GroupByAttributeKey);
            rawSeriesKeyExpr = "DataPointAttributes[{groupByKey:String}]";
            seriesKeyExpr = $"{rawSeriesKeyExpr} AS SeriesKey";
            seriesAttributesExpr = "map({groupByKey:String}, any(DataPointAttributes[{groupByKey:String}])) AS SeriesAttributes";
        }

        var whereSql = $"MetricName = {{metricName:String}} AND {filterSql.WhereSql}";
        // Outer SELECT ServiceName, SeriesKey FROM (...) wrapper, not the ranking query's
        // own three columns used directly as the IN subquery - the ranked-by/ORDER BY
        // column (RankValue) has to be part of the inner SELECT to sort by, but the outer
        // (ServiceName, SeriesKey) tuple this feeds into below is only 2 columns wide.
        // Selecting the inner query's three columns straight into that 2-column tuple IN
        // is a real column-count mismatch ClickHouse rejects outright (Code: 20,
        // NUMBER_OF_COLUMNS_DOESNT_MATCH) - caught only via a real ClickHouse instance
        // (see this project's own "no fake ClickHouse in unit tests" convention), not the
        // Assert.Contains SQL-substring tests this file already has, which never execute
        // the generated SQL.
        var topSeriesSql = "SELECT ServiceName, SeriesKey FROM (\n" +
            "  SELECT ServiceName, " +
            $"{rawSeriesKeyExpr} AS SeriesKey, {rankExpr} AS RankValue\n" +
            $"  FROM {table}\n" +
            $"  WHERE {whereSql}\n" +
            "  GROUP BY ServiceName, SeriesKey\n" +
            "  ORDER BY RankValue DESC\n" +
            "  LIMIT {topN:UInt32}\n" +
            ")";

        var sql = "SELECT toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart, " +
            $"ServiceName, {seriesKeyExpr}, {seriesAttributesExpr}, " +
            $"{valueSelect}\n" +
            $"FROM {table}\n" +
            $"WHERE {whereSql}\n" +
            $"  AND (ServiceName, {rawSeriesKeyExpr}) IN (\n{topSeriesSql}\n  )\n" +
            "GROUP BY BucketStart, ServiceName, SeriesKey\n" +
            "ORDER BY ServiceName, SeriesKey, BucketStart";

        return new MetricSeriesSql(sql, filterSql.Parameters, request.Type);
    }
}
