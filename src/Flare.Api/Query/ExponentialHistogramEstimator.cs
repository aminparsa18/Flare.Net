namespace Flare.Api.Query;

/// <summary>
/// One aggregated slice of <c>metrics_exponential_histogram</c> rows sharing a single
/// <see cref="Scale"/> - what <see cref="MetricSeriesQueryBuilder"/>/<see cref="MetricAlertConditionQueryBuilder"/>'s
/// ExponentialHistogram branches return per (group, <c>Scale</c>). Bucket counts are keyed by
/// absolute bucket index (the row's <c>Offset</c> already added in), not array position, so
/// slices with different offsets can be added directly. Counts are signed: a cumulative
/// series' previous point is subtracted as a negative contribution at its own scale (see
/// <see cref="HistogramTemporalitySql"/>), so a single slice can be negative until
/// <see cref="ExponentialHistogramEstimator.Merge"/> adds it to the rest.
/// </summary>
public sealed record ExponentialHistogramBuckets
{
    public required int Scale { get; init; }

    public required long Count { get; init; }

    public required double Sum { get; init; }

    public required long ZeroCount { get; init; }

    public required double ZeroThreshold { get; init; }

    public required IReadOnlyList<int> PositiveIndices { get; init; }

    public required IReadOnlyList<long> PositiveCounts { get; init; }

    public required IReadOnlyList<int> NegativeIndices { get; init; }

    public required IReadOnlyList<long> NegativeCounts { get; init; }

    /// <summary>Null when no folded row carried the optional OTLP <c>min</c>.</summary>
    public double? Min { get; init; }

    /// <summary>Null when no folded row carried the optional OTLP <c>max</c>.</summary>
    public double? Max { get; init; }
}

/// <summary>
/// Merges and estimates quantiles over OTLP exponential histograms - the counterpart of
/// <see cref="HistogramQuantileEstimator"/> for <c>metrics_exponential_histogram</c>. Pure, no
/// ClickHouse dependency. See ADR-0060.
/// </summary>
/// <remarks>
/// <para>
/// Bucket <c>i</c> at scale <c>s</c> covers <c>(base^i, base^(i+1)]</c>, <c>base = 2^(2^-s)</c>;
/// negative-range buckets use the same indexing on the absolute value. Lowering the scale by
/// one squares the base, so bucket <c>i</c> at scale <c>s</c> lies wholly inside bucket
/// <c>i &gt;&gt; 1</c> at scale <c>s - 1</c> - <see cref="Merge"/> brings every slice down to the
/// lowest scale present with an arithmetic right shift (floor division, correct for negative
/// indices too) and adds counts per index. Downscaling only ever loses resolution, never
/// correctness, which is why the lowest scale is the merge target rather than the highest.
/// Because downscaling is linear, a negated previous-point contribution cancels exactly against
/// the current point once both sit at the same scale; any count still below zero afterwards
/// (a malformed or out-of-order series) is clamped to zero rather than reported.
/// </para>
/// <para>
/// <see cref="Estimate"/> walks buckets in value order - negative buckets from the largest
/// magnitude down, then the zero bucket, then positive buckets upward - to the one holding the
/// target rank, and interpolates inside it on a log scale (<c>base^(i + fraction)</c>), which
/// matches how the bucket widths themselves grow, rather than linearly as
/// <see cref="HistogramQuantileEstimator"/> does for explicit bounds. The result is clamped to
/// the observed <see cref="ExponentialHistogramBuckets.Min"/>/<see cref="ExponentialHistogramBuckets.Max"/>
/// when present, so an estimate in the outermost bucket can't overshoot a real sample.
/// </para>
/// </remarks>
public static class ExponentialHistogramEstimator
{
    /// <summary>
    /// Folds same-group slices (typically one per distinct scale) into one histogram at the lowest
    /// scale among them, with every count clamped to be non-negative and empty buckets dropped.
    /// Returns <see langword="null"/> for an empty input.
    /// </summary>
    public static ExponentialHistogramBuckets? Merge(IReadOnlyList<ExponentialHistogramBuckets> slices)
    {
        if (slices.Count == 0)
        {
            return null;
        }

        var targetScale = slices.Min(s => s.Scale);
        var positive = new SortedDictionary<int, long>();
        var negative = new SortedDictionary<int, long>();
        long count = 0;
        long zeroCount = 0;
        double sum = 0;
        double zeroThreshold = 0;
        double? min = null;
        double? max = null;

        foreach (var slice in slices)
        {
            var shift = slice.Scale - targetScale;
            AddDownscaled(positive, slice.PositiveIndices, slice.PositiveCounts, shift);
            AddDownscaled(negative, slice.NegativeIndices, slice.NegativeCounts, shift);
            count += slice.Count;
            zeroCount += slice.ZeroCount;
            sum += slice.Sum;
            zeroThreshold = Math.Max(zeroThreshold, slice.ZeroThreshold);
            if (slice.Min is { } sliceMin)
            {
                min = min is { } m ? Math.Min(m, sliceMin) : sliceMin;
            }

            if (slice.Max is { } sliceMax)
            {
                max = max is { } m ? Math.Max(m, sliceMax) : sliceMax;
            }
        }

        var positiveBuckets = positive.Where(b => b.Value > 0).ToList();
        var negativeBuckets = negative.Where(b => b.Value > 0).ToList();
        return new ExponentialHistogramBuckets
        {
            Scale = targetScale,
            Count = Math.Max(count, 0),
            Sum = sum,
            ZeroCount = Math.Max(zeroCount, 0),
            ZeroThreshold = zeroThreshold,
            PositiveIndices = [.. positiveBuckets.Select(b => b.Key)],
            PositiveCounts = [.. positiveBuckets.Select(b => b.Value)],
            NegativeIndices = [.. negativeBuckets.Select(b => b.Key)],
            NegativeCounts = [.. negativeBuckets.Select(b => b.Value)],
            Min = min,
            Max = max,
        };
    }

    /// <summary>
    /// Returns the estimated value at <paramref name="quantile"/> (0.0-1.0), or
    /// <see langword="null"/> when there's nothing to estimate from (no bucketed observations)
    /// or the quantile is out of range.
    /// </summary>
    public static double? Estimate(ExponentialHistogramBuckets histogram, double quantile)
    {
        if (quantile is < 0 or > 1)
        {
            return null;
        }

        var total = Math.Max(histogram.ZeroCount, 0) + Total(histogram.PositiveCounts) + Total(histogram.NegativeCounts);
        if (total == 0)
        {
            return null;
        }

        var targetRank = quantile * total;
        double cumulative = 0;

        // Negative range, most negative first: the highest absolute index holds the values
        // furthest below zero. Bucket i spans [-(base^(i+1)), -(base^i)), so moving through it
        // toward zero means the exponent falls from i + 1 to i.
        for (var k = histogram.NegativeIndices.Count - 1; k >= 0; k--)
        {
            var bucketCount = histogram.NegativeCounts[k];
            if (bucketCount > 0 && cumulative + bucketCount >= targetRank)
            {
                var fraction = (targetRank - cumulative) / bucketCount;
                return Clamp(-BucketBoundary(histogram.Scale, histogram.NegativeIndices[k] + 1 - fraction), histogram);
            }

            cumulative += bucketCount;
        }

        if (histogram.ZeroCount > 0 && cumulative + histogram.ZeroCount >= targetRank)
        {
            return Clamp(0, histogram);
        }

        cumulative += histogram.ZeroCount;

        for (var k = 0; k < histogram.PositiveIndices.Count; k++)
        {
            var bucketCount = histogram.PositiveCounts[k];
            if (bucketCount > 0 && cumulative + bucketCount >= targetRank)
            {
                var fraction = (targetRank - cumulative) / bucketCount;
                return Clamp(BucketBoundary(histogram.Scale, histogram.PositiveIndices[k] + fraction), histogram);
            }

            cumulative += bucketCount;
        }

        // Floating-point rounding can leave targetRank fractionally past the running total at
        // quantile = 1.0 - answer with the top of the distribution rather than nothing.
        return EstimateMax(histogram);
    }

    /// <summary>
    /// The observed max when any folded row carried one; otherwise the upper boundary of the
    /// highest non-empty bucket, an approximation in the same spirit as
    /// <see cref="HistogramQuantileEstimator.EstimateMax"/>. <see langword="null"/> when empty.
    /// </summary>
    public static double? EstimateMax(ExponentialHistogramBuckets histogram)
    {
        if (histogram.Max is { } max)
        {
            return max;
        }

        for (var k = histogram.PositiveIndices.Count - 1; k >= 0; k--)
        {
            if (histogram.PositiveCounts[k] > 0)
            {
                return BucketBoundary(histogram.Scale, histogram.PositiveIndices[k] + 1);
            }
        }

        if (histogram.ZeroCount > 0)
        {
            return histogram.ZeroThreshold;
        }

        // Only negative values: the one closest to zero bounds the max from above.
        for (var k = 0; k < histogram.NegativeIndices.Count; k++)
        {
            if (histogram.NegativeCounts[k] > 0)
            {
                return -BucketBoundary(histogram.Scale, histogram.NegativeIndices[k]);
            }
        }

        return null;
    }

    /// <summary><c>base^exponent</c> for <c>base = 2^(2^-scale)</c>, i.e. <c>2^(exponent * 2^-scale)</c>.</summary>
    private static double BucketBoundary(int scale, double exponent) => Math.Pow(2, exponent * Math.ScaleB(1.0, -scale));

    private static void AddDownscaled(SortedDictionary<int, long> target, IReadOnlyList<int> indices, IReadOnlyList<long> counts, int shift)
    {
        var n = Math.Min(indices.Count, counts.Count);
        for (var k = 0; k < n; k++)
        {
            // Arithmetic shift: floor division by 2^shift, so -1 >> 1 == -1 (bucket (base^-1, 1]
            // lies inside the coarser bucket (base'^-1, 1]), not 0 as truncating division gives.
            var index = indices[k] >> shift;
            target[index] = target.GetValueOrDefault(index) + counts[k];
        }
    }

    /// <summary>Sum of the positive counts - <see cref="Merge"/> output has no others, but a raw slice may.</summary>
    private static long Total(IReadOnlyList<long> counts)
    {
        long total = 0;
        foreach (var count in counts)
        {
            total += Math.Max(count, 0);
        }

        return total;
    }

    private static double Clamp(double value, ExponentialHistogramBuckets histogram)
    {
        if (histogram.Min is { } min && value < min)
        {
            value = min;
        }

        if (histogram.Max is { } max && value > max)
        {
            value = max;
        }

        return value;
    }
}
