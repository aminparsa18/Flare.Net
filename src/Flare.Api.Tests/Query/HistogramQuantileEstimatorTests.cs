using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class HistogramQuantileEstimatorTests
{
    [Fact]
    public void Estimate_ReturnsNull_WhenBucketCountsIsEmpty()
    {
        Assert.Null(HistogramQuantileEstimator.Estimate([], [10.0], 0.5));
    }

    [Fact]
    public void Estimate_ReturnsNull_WhenExplicitBoundsIsEmpty()
    {
        // Degenerate but spec-valid: a single (-Inf, +Inf) bucket with no distribution -
        // no finite bound to anchor an estimate to.
        Assert.Null(HistogramQuantileEstimator.Estimate([5], [], 0.5));
    }

    [Fact]
    public void Estimate_ReturnsNull_WhenAllBucketsAreEmpty()
    {
        Assert.Null(HistogramQuantileEstimator.Estimate([0, 0, 0], [10.0, 50.0], 0.5));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Estimate_ReturnsNull_ForOutOfRangeQuantile(double quantile)
    {
        Assert.Null(HistogramQuantileEstimator.Estimate([1, 2, 3], [10.0, 50.0], quantile));
    }

    [Fact]
    public void Estimate_P50_OfUniformlyFilledMiddleBucket_InterpolatesLinearly()
    {
        // Buckets: (-Inf,10]=0, (10,50]=10, (50,+Inf)=0 -> all 10 observations in the
        // middle bucket, uniformly assumed across it. Median (5th of 10, 0-indexed
        // target rank 5) should land at the bucket's midpoint.
        var result = HistogramQuantileEstimator.Estimate([0, 10, 0], [10.0, 50.0], 0.5);

        Assert.Equal(30.0, result); // 10 + (5/10)*(50-10) = 30
    }

    [Fact]
    public void Estimate_FirstBucket_ReturnsItsUpperBound_NotExtrapolatedBelow()
    {
        // All observations in the first (-Inf, 10] bucket - no finite lower bound to
        // interpolate from, so the estimate clamps to the bucket's one finite edge.
        var result = HistogramQuantileEstimator.Estimate([10, 0, 0], [10.0, 50.0], 0.5);

        Assert.Equal(10.0, result);
    }

    [Fact]
    public void Estimate_LastBucket_ReturnsItsLowerBound_NotExtrapolatedAbove()
    {
        // All observations in the last (50, +Inf) bucket - no finite upper bound, clamps
        // to the bucket's one finite edge.
        var result = HistogramQuantileEstimator.Estimate([0, 0, 10], [10.0, 50.0], 0.5);

        Assert.Equal(50.0, result);
    }

    [Fact]
    public void Estimate_P99_OfSkewedData_ResolvingInsideTheLastBucket_ClampsToItsLowerBound()
    {
        // 90+5=95 after the first two buckets, so rank 99 of 100 resolves inside the
        // third (90,+Inf) bucket - genuinely mid-bucket, not just landing on its edge.
        // No finite upper bound to interpolate toward, so the fraction is discarded and
        // the result clamps to the bucket's one finite edge (its lower bound, 50).
        var result = HistogramQuantileEstimator.Estimate([90, 5, 5], [10.0, 50.0], 0.99);

        Assert.Equal(50.0, result);
    }

    [Fact]
    public void Estimate_P0_OfDataStartingInASecondBucket_ReturnsFirstBucketUpperBound()
    {
        // Quantile 0 -> target rank 0, immediately satisfied by the first bucket
        // regardless of its own count - exercises the "bucket has zero width to
        // interpolate across" branch (count == 0 at the resolving bucket), not just the
        // usual "landed partway through a populated bucket" path.
        var result = HistogramQuantileEstimator.Estimate([0, 5, 5], [10.0, 50.0], 0.0);

        Assert.Equal(10.0, result);
    }

    [Fact]
    public void Estimate_SingleBucket_WithOneExplicitBound_ReturnsThatBound()
    {
        var result = HistogramQuantileEstimator.Estimate([7], [25.0], 0.5);

        Assert.Equal(25.0, result);
    }

    [Fact]
    public void Estimate_MalformedInput_BucketCountsLongerThanBoundsPlusOne_ReturnsNull_NotThrows()
    {
        // 3 bucket counts should pair with exactly 2 bounds; only 1 given here.
        var result = HistogramQuantileEstimator.Estimate([1, 1, 100], [10.0], 0.99);

        Assert.Null(result);
    }
}
