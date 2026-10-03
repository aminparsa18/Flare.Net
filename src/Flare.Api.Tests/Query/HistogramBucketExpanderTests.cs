using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class HistogramBucketExpanderTests
{
    [Fact]
    public void Explicit_DropsEmptyBuckets_AndClosesOpenEnds()
    {
        // (-Inf,10] (10,50] (50,100] (100,+Inf)
        var b = HistogramBucketExpander.Expand([3UL, 0UL, 2UL, 1UL], [10.0, 50.0, 100.0]);

        Assert.Equal([0.0, 50.0, 100.0], b.Lowers);
        Assert.Equal([10.0, 100.0, 150.0], b.Uppers); // overflow bucket repeats the previous width (50)
        Assert.Equal([3.0, 2.0, 1.0], b.Counts);
    }

    [Fact]
    public void Explicit_NegativeFirstBound_KeepsItAsLowerEdge()
    {
        var b = HistogramBucketExpander.Expand([1UL, 0UL], [-5.0]);

        Assert.Equal([-5.0], b.Lowers);
        Assert.Equal([-5.0], b.Uppers);
    }

    [Fact]
    public void Explicit_SingleBound_OverflowUsesBoundAsWidth()
    {
        var b = HistogramBucketExpander.Expand([0UL, 4UL], [8.0]);

        Assert.Equal([8.0], b.Lowers);
        Assert.Equal([16.0], b.Uppers);
    }

    [Fact]
    public void Explicit_MalformedLayout_ReturnsNothing()
    {
        Assert.Empty(HistogramBucketExpander.Expand([1UL, 2UL], [10.0, 20.0]).Counts);
        Assert.Empty(HistogramBucketExpander.Expand([5UL], []).Counts);
    }

    [Fact]
    public void Exponential_OrdersNegativeZeroPositive_WithBaseBoundaries()
    {
        // scale 0 -> base 2: index 1 = (2,4], index 0 = (1,2]
        var b = HistogramBucketExpander.Expand(new ExponentialHistogramBuckets
        {
            Scale = 0,
            Count = 7,
            Sum = 0,
            ZeroCount = 1,
            ZeroThreshold = 0.5,
            PositiveIndices = [0, 1],
            PositiveCounts = [2, 3],
            NegativeIndices = [0],
            NegativeCounts = [1],
        });

        Assert.Equal([-2.0, -0.5, 1.0, 2.0], b.Lowers);
        Assert.Equal([-1.0, 0.5, 2.0, 4.0], b.Uppers);
        Assert.Equal([1.0, 1.0, 2.0, 3.0], b.Counts);
    }

    [Fact]
    public void WithBuckets_Null_LeavesPointUntouched()
    {
        var point = new MetricSeriesPoint { BucketStart = DateTimeOffset.UnixEpoch };
        Assert.Null(point.WithBuckets(null).BucketCounts);
    }
}
