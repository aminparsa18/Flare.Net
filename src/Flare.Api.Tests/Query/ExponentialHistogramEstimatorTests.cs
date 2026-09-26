using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExponentialHistogramEstimatorTests
{
    private const int Precision = 9;

    private static ExponentialHistogramBuckets Buckets(
        int scale,
        (int Index, long Count)[]? positive = null,
        (int Index, long Count)[]? negative = null,
        long zeroCount = 0,
        double zeroThreshold = 0,
        double? min = null,
        double? max = null)
    {
        positive ??= [];
        negative ??= [];
        return new ExponentialHistogramBuckets
        {
            Scale = scale,
            Count = zeroCount + positive.Sum(b => b.Count) + negative.Sum(b => b.Count),
            Sum = 0,
            ZeroCount = zeroCount,
            ZeroThreshold = zeroThreshold,
            PositiveIndices = [.. positive.Select(b => b.Index)],
            PositiveCounts = [.. positive.Select(b => b.Count)],
            NegativeIndices = [.. negative.Select(b => b.Index)],
            NegativeCounts = [.. negative.Select(b => b.Count)],
            Min = min,
            Max = max,
        };
    }

    [Fact]
    public void Merge_ReturnsNull_ForNoSlices()
    {
        Assert.Null(ExponentialHistogramEstimator.Merge([]));
    }

    [Fact]
    public void Merge_OfOneSlice_KeepsItsBuckets_AndDropsEmptyOnes()
    {
        var merged = ExponentialHistogramEstimator.Merge([Buckets(3, positive: [(5, 2), (6, 0)])])!;

        Assert.Equal(3, merged.Scale);
        Assert.Equal([5], merged.PositiveIndices);
        Assert.Equal([2L], merged.PositiveCounts);
    }

    [Fact]
    public void Merge_CancelsANegatedPreviousPoint_AtADifferentScale()
    {
        // A cumulative series: previous point at scale 2 had buckets 4 and 5 (= scale-1 bucket
        // 2); the current point, after a rescale to 1, has bucket 2 = 5 and bucket 3 = 1. The
        // increase is 3 in bucket 2 and 1 in bucket 3.
        var current = Buckets(1, positive: [(2, 5), (3, 1)]);
        var previous = Buckets(2, positive: [(4, -1), (5, -1)]);

        var merged = ExponentialHistogramEstimator.Merge([current, previous])!;

        Assert.Equal(1, merged.Scale);
        Assert.Equal([2, 3], merged.PositiveIndices);
        Assert.Equal([3L, 1L], merged.PositiveCounts);
        Assert.Equal(4L, merged.Count);
    }

    [Fact]
    public void Merge_ClampsCountsThatStayNegative()
    {
        var merged = ExponentialHistogramEstimator.Merge([Buckets(0, positive: [(0, 2), (1, -3)], zeroCount: -1)])!;

        Assert.Equal([0], merged.PositiveIndices);
        Assert.Equal(0L, merged.ZeroCount);
        Assert.Equal(0L, merged.Count);
    }

    [Fact]
    public void Merge_DownscalesToTheLowestScale_BeforeAddingCounts()
    {
        // Scale 2 buckets 4-7 fold pairwise into scale 1 buckets 2 and 3.
        var fine = Buckets(2, positive: [(4, 1), (5, 1), (6, 1), (7, 1)]);
        var coarse = Buckets(1, positive: [(2, 3)]);

        var merged = ExponentialHistogramEstimator.Merge([fine, coarse])!;

        Assert.Equal(1, merged.Scale);
        Assert.Equal([2, 3], merged.PositiveIndices);
        Assert.Equal([5L, 2L], merged.PositiveCounts);
        Assert.Equal(7L, merged.Count);
    }

    [Fact]
    public void Merge_FloorsNegativeIndices_WhenDownscaling()
    {
        // Bucket -1 at scale 1 is (2^-0.5, 1], inside scale 0's bucket -1 = (0.5, 1] -
        // truncating division would wrongly put it in bucket 0 = (1, 2].
        var fine = Buckets(1, positive: [(-3, 1), (-1, 1)]);
        var coarse = Buckets(0, positive: [(0, 1)]);

        var merged = ExponentialHistogramEstimator.Merge([fine, coarse])!;

        Assert.Equal([-2, -1, 0], merged.PositiveIndices);
        Assert.Equal([1L, 1L, 1L], merged.PositiveCounts);
    }

    [Fact]
    public void Merge_SumsZeroCounts_TakesWidestThreshold_AndCombinesMinMax()
    {
        var a = Buckets(0, zeroCount: 2, zeroThreshold: 1e-6, min: -1, max: 3);
        var b = Buckets(1, zeroCount: 1, zeroThreshold: 1e-3, min: null, max: 7) with { Sum = 4.5 };

        var merged = ExponentialHistogramEstimator.Merge([a, b])!;

        Assert.Equal(3L, merged.ZeroCount);
        Assert.Equal(1e-3, merged.ZeroThreshold);
        Assert.Equal(-1, merged.Min);
        Assert.Equal(7, merged.Max);
        Assert.Equal(4.5, merged.Sum);
    }

    [Fact]
    public void Estimate_ReturnsNull_WhenThereIsNoData()
    {
        Assert.Null(ExponentialHistogramEstimator.Estimate(Buckets(0), 0.5));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Estimate_ReturnsNull_ForAnOutOfRangeQuantile(double quantile)
    {
        Assert.Null(ExponentialHistogramEstimator.Estimate(Buckets(0, positive: [(0, 1)]), quantile));
    }

    [Fact]
    public void Estimate_InterpolatesOnALogScale_InsideTheBucket()
    {
        // Scale 0: bucket 0 = (1, 2]. Halfway through it on a log scale is 2^0.5, not 1.5.
        var value = ExponentialHistogramEstimator.Estimate(Buckets(0, positive: [(0, 10)]), 0.5);

        Assert.Equal(Math.Sqrt(2), value!.Value, Precision);
    }

    [Fact]
    public void Estimate_WalksToTheBucketHoldingTheRank()
    {
        // Rank 1.5 of 2 lands halfway through bucket 1 = (2, 4].
        var value = ExponentialHistogramEstimator.Estimate(Buckets(0, positive: [(0, 1), (1, 1)]), 0.75);

        Assert.Equal(Math.Pow(2, 1.5), value!.Value, Precision);
    }

    [Fact]
    public void Estimate_UsesTheScaleForTheBase()
    {
        // Scale 1: base = 2^0.5, bucket 2 = (2, 2^1.5]; halfway = 2^1.25.
        var value = ExponentialHistogramEstimator.Estimate(Buckets(1, positive: [(2, 4)]), 0.5);

        Assert.Equal(Math.Pow(2, 1.25), value!.Value, Precision);
    }

    [Fact]
    public void Estimate_ReturnsZero_WhenTheRankFallsInTheZeroBucket()
    {
        var histogram = Buckets(0, positive: [(0, 5)], zeroCount: 5);

        Assert.Equal(0, ExponentialHistogramEstimator.Estimate(histogram, 0.4));
    }

    [Fact]
    public void Estimate_OrdersNegativeBucketsBeforePositiveOnes()
    {
        // Negative bucket 1 = [-4, -2), positive bucket 0 = (1, 2].
        var histogram = Buckets(0, positive: [(0, 2)], negative: [(1, 2)]);

        Assert.Equal(-Math.Pow(2, 1.5), ExponentialHistogramEstimator.Estimate(histogram, 0.25)!.Value, Precision);
        Assert.Equal(Math.Sqrt(2), ExponentialHistogramEstimator.Estimate(histogram, 0.75)!.Value, Precision);
    }

    [Fact]
    public void Estimate_WalksNegativeBucketsFromTheLargestMagnitude()
    {
        // Negative bucket 2 = [-8, -4) holds the lowest values, so it comes first.
        var histogram = Buckets(0, negative: [(0, 1), (2, 1)]);

        Assert.Equal(-Math.Pow(2, 2.5), ExponentialHistogramEstimator.Estimate(histogram, 0.25)!.Value, Precision);
    }

    [Fact]
    public void Estimate_ClampsToTheObservedMinAndMax()
    {
        var histogram = Buckets(0, positive: [(0, 10)], min: 1.9, max: 1.95);

        Assert.Equal(1.95, ExponentialHistogramEstimator.Estimate(histogram, 0.99));
        Assert.Equal(1.9, ExponentialHistogramEstimator.Estimate(histogram, 0.1));
    }

    [Fact]
    public void EstimateMax_PrefersTheObservedMax()
    {
        Assert.Equal(1.5, ExponentialHistogramEstimator.EstimateMax(Buckets(0, positive: [(0, 1)], max: 1.5)));
    }

    [Fact]
    public void EstimateMax_FallsBackToTheHighestNonEmptyBucketsUpperBound()
    {
        Assert.Equal(4, ExponentialHistogramEstimator.EstimateMax(Buckets(0, positive: [(0, 1), (1, 1)])));
    }

    [Fact]
    public void EstimateMax_ForOnlyNegativeValues_IsTheBoundClosestToZero()
    {
        Assert.Equal(-2, ExponentialHistogramEstimator.EstimateMax(Buckets(0, negative: [(1, 1), (3, 1)])));
    }

    [Fact]
    public void EstimateMax_ReturnsNull_WhenThereIsNoData()
    {
        Assert.Null(ExponentialHistogramEstimator.EstimateMax(Buckets(0)));
    }
}
