using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Turns one time bucket's merged histogram into explicit <c>(lower, upper, count)</c> triples for
/// the dashboard's heatmap (<see cref="MetricQueryRequest.IncludeBuckets"/>). Pure, no ClickHouse
/// dependency. Only non-empty buckets are returned, ascending by value.
/// </summary>
/// <remarks>
/// A heatmap needs finite cell edges, which OTLP's open-ended buckets don't have. Explicit
/// histograms close the first bucket at <c>min(0, bounds[0])</c> (latency-like data is
/// non-negative, so <c>(-Inf, bounds[0]]</c> draws as <c>[0, bounds[0]]</c>) and the overflow
/// bucket one bucket-width past the last bound (the previous bucket's width, or the bound itself
/// when there is only one). Exponential histograms are exact: bucket <c>i</c> at scale <c>s</c> is
/// <c>(base^i, base^(i+1)]</c>, negative buckets mirror it, and the zero bucket spans
/// <c>[-ZeroThreshold, ZeroThreshold]</c>.
/// </remarks>
public static class HistogramBucketExpander
{
    public readonly record struct Buckets(IReadOnlyList<double> Lowers, IReadOnlyList<double> Uppers, IReadOnlyList<double> Counts);

    public static Buckets Expand(IReadOnlyList<ulong> bucketCounts, IReadOnlyList<double> explicitBounds)
    {
        var lowers = new List<double>();
        var uppers = new List<double>();
        var counts = new List<double>();

        // No bounds = one all-encompassing bucket with nothing to anchor a cell to; a layout
        // that disagrees with the OTLP "bounds + 1" rule is malformed (see HistogramQuantileEstimator).
        if (explicitBounds.Count == 0 || bucketCounts.Count != explicitBounds.Count + 1)
        {
            return new Buckets(lowers, uppers, counts);
        }

        for (var i = 0; i < bucketCounts.Count; i++)
        {
            if (bucketCounts[i] == 0)
            {
                continue;
            }

            double lower;
            double upper;
            if (i == 0)
            {
                lower = Math.Min(0, explicitBounds[0]);
                upper = explicitBounds[0];
            }
            else if (i == explicitBounds.Count)
            {
                var last = explicitBounds[^1];
                var width = explicitBounds.Count > 1 ? last - explicitBounds[^2] : Math.Abs(last);
                lower = last;
                upper = last + (width > 0 ? width : 1);
            }
            else
            {
                lower = explicitBounds[i - 1];
                upper = explicitBounds[i];
            }

            lowers.Add(lower);
            uppers.Add(upper);
            counts.Add(bucketCounts[i]);
        }

        return new Buckets(lowers, uppers, counts);
    }

    public static Buckets Expand(ExponentialHistogramBuckets histogram)
    {
        var lowers = new List<double>();
        var uppers = new List<double>();
        var counts = new List<double>();

        // Most negative first: the highest absolute index is furthest below zero.
        for (var k = histogram.NegativeIndices.Count - 1; k >= 0; k--)
        {
            if (histogram.NegativeCounts[k] <= 0)
            {
                continue;
            }

            lowers.Add(-Boundary(histogram.Scale, histogram.NegativeIndices[k] + 1));
            uppers.Add(-Boundary(histogram.Scale, histogram.NegativeIndices[k]));
            counts.Add(histogram.NegativeCounts[k]);
        }

        if (histogram.ZeroCount > 0)
        {
            lowers.Add(-histogram.ZeroThreshold);
            uppers.Add(histogram.ZeroThreshold);
            counts.Add(histogram.ZeroCount);
        }

        for (var k = 0; k < histogram.PositiveIndices.Count; k++)
        {
            if (histogram.PositiveCounts[k] <= 0)
            {
                continue;
            }

            lowers.Add(Boundary(histogram.Scale, histogram.PositiveIndices[k]));
            uppers.Add(Boundary(histogram.Scale, histogram.PositiveIndices[k] + 1));
            counts.Add(histogram.PositiveCounts[k]);
        }

        return new Buckets(lowers, uppers, counts);
    }

    /// <summary>Copies <paramref name="buckets"/> onto the point; null leaves it untouched (buckets weren't requested).</summary>
    public static MetricSeriesPoint WithBuckets(this MetricSeriesPoint point, Buckets? buckets) =>
        buckets is { } b ? point with { BucketLowers = b.Lowers, BucketUppers = b.Uppers, BucketCounts = b.Counts } : point;

    private static double Boundary(int scale, double exponent) => Math.Pow(2, exponent * Math.ScaleB(1.0, -scale));
}
