namespace Flare.Api.Query;

/// <summary>
/// Approximates a quantile from OTLP explicit-bounds histogram bucket data via linear
/// interpolation within the bucket containing the target rank - the same method
/// Prometheus/Grafana's <c>histogram_quantile</c> uses for classic histograms. Pure,
/// no ClickHouse dependency - <see cref="MetricQueryService"/> is the only caller,
/// feeding it the per-bucket <c>sumForEach(BucketCounts)</c>/<c>any(ExplicitBounds)</c>
/// results <see cref="MetricSeriesQueryBuilder"/> selects for histogram series.
/// </summary>
/// <remarks>
/// This is the second of the two named, deliberately-unresolved v1 limitations
/// Planning.md's v6 flags: the estimate assumes <paramref name="explicitBounds"/> is
/// stable across every row folded into the bucket by <c>sumForEach</c> (true for one
/// metric name's histogram in practice, since an instrument's bucket boundaries are
/// fixed at creation - but not something this method can verify from the aggregated
/// arrays alone).
///
/// Per the OTLP spec, bucket <c>i</c> covers <c>(explicitBounds[i-1], explicitBounds[i]]</c>
/// for interior buckets, <c>(-Inf, explicitBounds[0]]</c> for the first, and
/// <c>(explicitBounds[^1], +Inf)</c> for the last. Neither open end can be interpolated
/// against meaningfully, so both edge cases collapse to returning the one finite bound
/// that bucket does have (<c>explicitBounds[0]</c> for the first, <c>explicitBounds[^1]</c>
/// for the last) rather than extrapolating past it - the same practical clamp most
/// dashboards apply, in place of Prometheus's own choice to return <c>+Inf</c> for a rank
/// that lands in the top bucket.
/// </remarks>
public static class HistogramQuantileEstimator
{
    /// <summary>
    /// Returns the estimated value at <paramref name="quantile"/> (0.0-1.0), or
    /// <see langword="null"/> when there's no data to estimate from (every bucket count
    /// is zero) or the inputs are malformed (no buckets, or an out-of-range quantile).
    /// </summary>
    public static double? Estimate(IReadOnlyList<ulong> bucketCounts, IReadOnlyList<double> explicitBounds, double quantile)
    {
        // explicitBounds.Count == 0 covers both "no data" and the degenerate valid OTLP
        // shape of a single all-encompassing (-Inf, +Inf) bucket (bucketCounts.Count == 1,
        // explicitBounds.Count == 0, per the spec's "bucket_counts = bounds + 1, except
        // both zero" rule) - there's no finite bound to anchor an estimate to either way,
        // so both return null honestly rather than guessing or indexing out of range.
        if (bucketCounts.Count == 0 || explicitBounds.Count == 0 || quantile is < 0 or > 1)
        {
            return null;
        }

        var total = 0UL;
        foreach (var count in bucketCounts)
        {
            total += count;
        }

        if (total == 0)
        {
            return null;
        }

        var targetRank = quantile * total;
        double cumulative = 0;

        for (var i = 0; i < bucketCounts.Count; i++)
        {
            var count = bucketCounts[i];
            var cumulativeAfter = cumulative + count;

            if (cumulativeAfter >= targetRank)
            {
                if (i > 0 && i - 1 >= explicitBounds.Count)
                {
                    // Malformed input - bucketCounts/explicitBounds lengths disagree
                    // beyond the spec's "+1" relationship (e.g. sumForEach folded rows
                    // from an instrument whose bucket layout actually changed, which
                    // this method's remarks already flag as an assumed-stable input).
                    // No bound to anchor to for this bucket - bail out rather than throw.
                    return null;
                }

                // First bucket has no finite lower bound; last bucket has no finite
                // upper bound - both collapse to the one finite edge this bucket does
                // have, per this method's remarks.
                var lower = i == 0 ? explicitBounds[0] : explicitBounds[i - 1];
                var upper = i < explicitBounds.Count ? explicitBounds[i] : lower;

                if (count == 0)
                {
                    return upper;
                }

                var fraction = (targetRank - cumulative) / count;
                return lower + (fraction * (upper - lower));
            }

            cumulative = cumulativeAfter;
        }

        // Floating-point rounding can put targetRank fractionally past the running total
        // (e.g. quantile = 1.0 exactly) - fall back to the last known bound rather than
        // falling through with no answer.
        return explicitBounds[^1];
    }

    /// <summary>
    /// Approximates the maximum observed value as the upper bound of the highest
    /// non-empty bucket - not a true max. The real OTLP <c>HistogramDataPoint.Max</c> is
    /// an optional field many SDKs (including .NET's own OTel histogram instrumentation)
    /// leave unset, and it isn't captured by the ingest pipeline; this derives an honest
    /// approximation from data already fetched for <see cref="Estimate"/> instead, at the
    /// cost of being a bucket boundary rather than an actual sample value. Callers/UI
    /// should present the result as approximate (see <see cref="Model.MetricSeriesPoint.MaxApprox"/>),
    /// never as a bare "Max".
    /// </summary>
    /// <returns>
    /// The upper bound of the highest bucket with a non-zero count, or <see langword="null"/>
    /// when there's no data (every bucket count is zero) or the inputs are malformed (no
    /// buckets, or a <paramref name="bucketCounts"/>/<paramref name="explicitBounds"/>
    /// length mismatch beyond the OTLP spec's "+1" relationship).
    /// </returns>
    public static double? EstimateMax(IReadOnlyList<ulong> bucketCounts, IReadOnlyList<double> explicitBounds)
    {
        if (bucketCounts.Count == 0 || explicitBounds.Count == 0)
        {
            return null;
        }

        for (var i = bucketCounts.Count - 1; i >= 0; i--)
        {
            if (bucketCounts[i] == 0)
            {
                continue;
            }

            // Last bucket has no finite upper bound per the OTLP spec - clamp to the
            // last finite bound instead, same edge-case clamp Estimate applies to its own
            // last-bucket case.
            var isLastBucket = i == bucketCounts.Count - 1;
            if (!isLastBucket && i >= explicitBounds.Count)
            {
                // Malformed input - bucketCounts/explicitBounds lengths disagree beyond
                // the spec's "+1" relationship. No bound to anchor to - bail out rather
                // than throw, same choice Estimate makes for the same situation.
                return null;
            }

            return i < explicitBounds.Count ? explicitBounds[i] : explicitBounds[^1];
        }

        // All buckets empty.
        return null;
    }
}
