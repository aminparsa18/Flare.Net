namespace Flare.Api.Query;

/// <summary>
/// Temporality-aware SQL fragments for the two histogram tables, shared by
/// <see cref="MetricSeriesQueryBuilder"/> (per time bucket) and
/// <see cref="MetricAlertConditionQueryBuilder"/> (one scalar per window). See ADR-0060.
/// </summary>
/// <remarks>
/// <para>
/// A delta-temporality point is already "what happened since the previous export", so its
/// counts are summed as-is. A cumulative point (the .NET SDK's default, and every
/// Prometheus-scraped histogram) is a running total since the series started, so summing
/// its rows over-counts - the defect ADR-0060 fixes. Each cumulative row instead contributes
/// its difference from the series' previous row, the same windowed
/// <c>row_number()</c>/<c>lagInFrame()</c> approach Sum uses (ADR-0035/ADR-0044), partitioned
/// by the full (<c>ServiceName</c>, <c>toString(DataPointAttributes)</c>) series identity:
/// </para>
/// <list type="bullet">
/// <item>The series' first row in the queried range contributes nothing - there's no earlier
/// row to diff against, and counting it would add the series' whole lifetime.</item>
/// <item>A reset contributes the row as-is: a changed <c>StartTime</c> (OTLP's own reset signal),
/// a count lower than the previous one, or (explicit buckets) a changed bucket layout.</item>
/// <item>Otherwise the row minus the previous row.</item>
/// </list>
/// <para>
/// Explicit-bucket rows diff element-wise. Exponential rows can't: the previous row may be at a
/// different scale, with different offsets. So a cumulative exponential row emits two
/// contributions - itself with <c>Sign = 1</c>, and the previous row with <c>Sign = -1</c> at the
/// previous row's own scale - and the signed per-scale slices are downscaled to a common scale
/// and added by <see cref="ExponentialHistogramEstimator.Merge"/>, where they cancel exactly
/// (downscaling is linear). A cumulative row's <c>Min</c>/<c>Max</c> cover the series' whole
/// lifetime, not the queried range, so they're dropped (NULL) rather than reported.
/// </para>
/// </remarks>
internal static class HistogramTemporalitySql
{
    private const string Window = "WINDOW w AS (PARTITION BY ServiceName, toString(DataPointAttributes) ORDER BY Time)";

    private const string IsDelta = "AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA'";

    private const string IsExplicitReset = "(StartTime != PrevStartTime OR Count < PrevCount OR length(BucketCounts) != length(PrevBucketCounts))";

    /// <summary>
    /// <c>WITH ranked AS (...)</c> over <c>metrics_histogram</c>, carrying <paramref name="keyColumns"/>
    /// plus each row's previous-row values - feed it to <see cref="ExplicitAggregates"/>.
    /// </summary>
    public static string ExplicitRankedCte(string table, string whereSql, string keyColumns) =>
        "WITH ranked AS (\n" +
        $"  SELECT {keyColumns}, ExplicitBounds, AggregationTemporality, StartTime, Count, Sum, BucketCounts,\n" +
        "    row_number() OVER w AS SeriesRowNum,\n" +
        "    lagInFrame(StartTime) OVER w AS PrevStartTime,\n" +
        "    lagInFrame(Count) OVER w AS PrevCount,\n" +
        "    lagInFrame(Sum) OVER w AS PrevSum,\n" +
        "    lagInFrame(BucketCounts) OVER w AS PrevBucketCounts\n" +
        $"  FROM {table}\n" +
        $"  WHERE {whereSql}\n" +
        $"  {Window}\n" +
        ")\n";

    /// <summary>
    /// Aggregates over <see cref="ExplicitRankedCte"/>: CountTotal (UInt64), SumTotal (Float64),
    /// BucketCountsTotal (Array(UInt64)), ExplicitBounds (Array(Float64)), in that order. The
    /// <c>toUInt64</c> wraps keep every <c>multiIf</c> branch one type - ClickHouse widens
    /// <c>UInt64 - UInt64</c> to <c>Int64</c>, and the reset guard means a diff is never negative.
    /// </summary>
    public const string ExplicitAggregates =
        $"sum(multiIf({IsDelta}, Count, SeriesRowNum = 1, toUInt64(0), {IsExplicitReset}, Count, toUInt64(Count - PrevCount))) AS CountTotal, " +
        $"sum(multiIf({IsDelta}, Sum, SeriesRowNum = 1, 0, {IsExplicitReset}, Sum, Sum - PrevSum)) AS SumTotal, " +
        $"sumForEach(multiIf({IsDelta}, BucketCounts, SeriesRowNum = 1, arrayWithConstant(length(BucketCounts), toUInt64(0)), {IsExplicitReset}, BucketCounts, " +
        "arrayMap((c, p) -> toUInt64(if(c >= p, c - p, 0)), BucketCounts, PrevBucketCounts))) AS BucketCountsTotal, " +
        "any(ExplicitBounds) AS ExplicitBounds";

    /// <summary>
    /// <c>WITH ranked AS (...), contributions AS (...)</c> over <c>metrics_exponential_histogram</c>:
    /// one signed contribution per delta row or cumulative row, plus a negated previous-row
    /// contribution for each non-reset cumulative row. <paramref name="keyColumns"/> must be plain
    /// column names/aliases of <c>ranked</c> (they're re-selected from it) - computed keys such as
    /// a series key or time bucket go in <paramref name="keyExpressions"/>, which is selected in
    /// <c>ranked</c> alongside them. Feed the result to <see cref="ExponentialAggregates"/>.
    /// </summary>
    public static string ExponentialContributionsCte(string table, string whereSql, string keyExpressions, string keyColumns) =>
        "WITH ranked AS (\n" +
        $"  SELECT {keyExpressions}, AggregationTemporality, StartTime, Count, Sum, Scale, ZeroCount, ZeroThreshold,\n" +
        "    PositiveOffset, PositiveBucketCounts, NegativeOffset, NegativeBucketCounts, Min, Max,\n" +
        "    row_number() OVER w AS SeriesRowNum,\n" +
        "    lagInFrame(StartTime) OVER w AS PrevStartTime,\n" +
        "    lagInFrame(Count) OVER w AS PrevCount,\n" +
        "    lagInFrame(Sum) OVER w AS PrevSum,\n" +
        "    lagInFrame(Scale) OVER w AS PrevScale,\n" +
        "    lagInFrame(ZeroCount) OVER w AS PrevZeroCount,\n" +
        "    lagInFrame(PositiveOffset) OVER w AS PrevPositiveOffset,\n" +
        "    lagInFrame(PositiveBucketCounts) OVER w AS PrevPositiveBucketCounts,\n" +
        "    lagInFrame(NegativeOffset) OVER w AS PrevNegativeOffset,\n" +
        "    lagInFrame(NegativeBucketCounts) OVER w AS PrevNegativeBucketCounts\n" +
        $"  FROM {table}\n" +
        $"  WHERE {whereSql}\n" +
        $"  {Window}\n" +
        "),\n" +
        "contributions AS (\n" +
        $"  SELECT {keyColumns}, toInt8(1) AS Sign, Count, Sum, Scale, ZeroCount, ZeroThreshold,\n" +
        "    PositiveOffset, PositiveBucketCounts, NegativeOffset, NegativeBucketCounts,\n" +
        $"    if({IsDelta}, Min, CAST(NULL AS Nullable(Float64))) AS RowMin,\n" +
        $"    if({IsDelta}, Max, CAST(NULL AS Nullable(Float64))) AS RowMax\n" +
        "  FROM ranked\n" +
        $"  WHERE {IsDelta} OR SeriesRowNum > 1\n" +
        "  UNION ALL\n" +
        $"  SELECT {keyColumns}, toInt8(-1), PrevCount, PrevSum, PrevScale, PrevZeroCount, toFloat64(0),\n" +
        "    PrevPositiveOffset, PrevPositiveBucketCounts, PrevNegativeOffset, PrevNegativeBucketCounts,\n" +
        "    CAST(NULL AS Nullable(Float64)), CAST(NULL AS Nullable(Float64))\n" +
        "  FROM ranked\n" +
        $"  WHERE NOT ({IsDelta}) AND SeriesRowNum > 1 AND StartTime = PrevStartTime AND Count >= PrevCount\n" +
        ")\n";

    /// <summary>
    /// The per-(group, <c>Scale</c>) aggregates over <see cref="ExponentialContributionsCte"/> -
    /// read back by <see cref="ExponentialHistogramRowReader"/> in this order: CountTotal,
    /// SumTotal, Scale, ZeroCountTotal, ZeroThresholdMax, PositiveIndices, PositiveCounts,
    /// NegativeIndices, NegativeCounts, MinValue, MaxValue. Requires <c>Scale</c> in the caller's
    /// <c>GROUP BY</c>. Counts are signed (Int64) since a slice may hold only negated previous-row
    /// contributions; bucket keys are absolute indices (<c>Offset + position</c>), wrapped in
    /// <c>toInt32</c> because ClickHouse widens <c>Int32 + UInt32</c> to <c>Int64</c>. <c>sumMap</c>
    /// returns a (keys, values) tuple, split with <c>tupleElement</c> so the driver reads two
    /// plain arrays.
    /// </summary>
    public const string ExponentialAggregates =
        "sum(Sign * toInt64(Count)) AS CountTotal, sum(Sign * Sum) AS SumTotal, Scale, " +
        "sum(Sign * toInt64(ZeroCount)) AS ZeroCountTotal, max(ZeroThreshold) AS ZeroThresholdMax, " +
        "tupleElement(sumMap(arrayMap(i -> toInt32(PositiveOffset + i - 1), arrayEnumerate(PositiveBucketCounts)), " +
        "arrayMap(c -> Sign * toInt64(c), PositiveBucketCounts)) AS PositiveBuckets, 1) AS PositiveIndices, " +
        "tupleElement(PositiveBuckets, 2) AS PositiveCounts, " +
        "tupleElement(sumMap(arrayMap(i -> toInt32(NegativeOffset + i - 1), arrayEnumerate(NegativeBucketCounts)), " +
        "arrayMap(c -> Sign * toInt64(c), NegativeBucketCounts)) AS NegativeBuckets, 1) AS NegativeIndices, " +
        "tupleElement(NegativeBuckets, 2) AS NegativeCounts, " +
        "min(RowMin) AS MinValue, max(RowMax) AS MaxValue";
}
