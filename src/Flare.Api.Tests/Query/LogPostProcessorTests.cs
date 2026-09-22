using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogPostProcessorTests
{
    private static readonly DateTimeOffset Bucket0 = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    private static LogAggregateBucket Bucket(int minute, double count, string? groupKey = null) =>
        new() { BucketStart = Bucket0.AddMinutes(minute), GroupKey = groupKey, Count = count };

    [Fact]
    public void Apply_NoFunctions_ReturnsSameReference()
    {
        var buckets = new[] { Bucket(0, 1) };

        var result = LogPostProcessor.Apply(buckets, []);

        Assert.Same(buckets, result);
    }

    [Fact]
    public void Apply_ClampMin_RaisesValuesBelowThreshold()
    {
        var buckets = new[] { Bucket(0, -5), Bucket(1, 3) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.ClampMin, Value = 0 }]);

        Assert.Equal([0, 3], result.Select(b => b.Count));
    }

    [Fact]
    public void Apply_ClampMax_LowersValuesAboveThreshold()
    {
        var buckets = new[] { Bucket(0, 150), Bucket(1, 3) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.ClampMax, Value = 100 }]);

        Assert.Equal([100, 3], result.Select(b => b.Count));
    }

    [Theory]
    [InlineData(LogPostProcessFunctionType.ClampMin)]
    [InlineData(LogPostProcessFunctionType.ClampMax)]
    public void Apply_ClampWithoutValue_Throws(LogPostProcessFunctionType type)
    {
        var buckets = new[] { Bucket(0, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = type, Value = null }]));
    }

    [Fact]
    public void Apply_Absolute_NegatesNegativeValues()
    {
        var buckets = new[] { Bucket(0, -4.5), Bucket(1, 4.5) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.Absolute }]);

        Assert.Equal([4.5, 4.5], result.Select(b => b.Count));
    }

    [Fact]
    public void Apply_Log2_ComputesBinaryLog()
    {
        var buckets = new[] { Bucket(0, 8) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.Log2 }]);

        Assert.Equal(3, result[0].Count);
    }

    [Fact]
    public void Apply_Log10_ComputesDecimalLog()
    {
        var buckets = new[] { Bucket(0, 1000) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.Log10 }]);

        Assert.Equal(3, result[0].Count);
    }

    [Theory]
    [InlineData(LogPostProcessFunctionType.Log2)]
    [InlineData(LogPostProcessFunctionType.Log10)]
    public void Apply_LogOfNonPositive_ReturnsZeroNotNaN(LogPostProcessFunctionType type)
    {
        var buckets = new[] { Bucket(0, 0), Bucket(1, -1) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = type }]);

        Assert.All(result, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void Apply_CumulativeSum_AccumulatesRunningTotal()
    {
        var buckets = new[] { Bucket(0, 1), Bucket(1, 2), Bucket(2, 3) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.CumulativeSum }]);

        Assert.Equal([1, 3, 6], result.Select(b => b.Count));
    }

    [Fact]
    public void Apply_PreservesBucketStart()
    {
        var buckets = new[] { Bucket(0, 1), Bucket(5, 2) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.Absolute }]);

        Assert.Equal([Bucket0, Bucket0.AddMinutes(5)], result.Select(b => b.BucketStart));
    }

    [Fact]
    public void Apply_EwmaSmoothing_WeightsRecentPointsMoreHeavily()
    {
        // windowSize=3 -> alpha = 2/(3+1) = 0.5. ewma[0]=10, ewma[1]=0.5*20+0.5*10=15,
        // ewma[2]=0.5*30+0.5*15=22.5.
        var buckets = new[] { Bucket(0, 10), Bucket(1, 20), Bucket(2, 30) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.EwmaSmoothing, WindowSize = 3 }]);

        Assert.Equal([10, 15, 22.5], result.Select(b => b.Count));
    }

    [Theory]
    [InlineData(LogPostProcessFunctionType.EwmaSmoothing)]
    [InlineData(LogPostProcessFunctionType.MedianSmoothing)]
    public void Apply_SmoothingWithoutWindowSize_Throws(LogPostProcessFunctionType type)
    {
        var buckets = new[] { Bucket(0, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = type, WindowSize = null }]));
    }

    [Theory]
    [InlineData(LogPostProcessFunctionType.EwmaSmoothing)]
    [InlineData(LogPostProcessFunctionType.MedianSmoothing)]
    public void Apply_SmoothingWithNonPositiveWindowSize_Throws(LogPostProcessFunctionType type)
    {
        var buckets = new[] { Bucket(0, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = type, WindowSize = 0 }]));
    }

    [Fact]
    public void Apply_MedianSmoothing_ReturnsMedianOfTrailingWindow()
    {
        // window=3, trailing: [1] -> 1; [1,5] -> 3; [1,5,3] -> 3; [5,3,9] -> 5.
        var buckets = new[] { Bucket(0, 1), Bucket(1, 5), Bucket(2, 3), Bucket(3, 9) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.MedianSmoothing, WindowSize = 3 }]);

        Assert.Equal([1, 3, 3, 5], result.Select(b => b.Count));
    }

    [Fact]
    public void Apply_ChainsFunctionsInOrder()
    {
        // abs(-9) = 9, then clampMax(9, 5) = 5 - order matters: clampMax first would give
        // clampMax(-9, 5) = -9, then abs(-9) = 9, a different result.
        var buckets = new[] { Bucket(0, -9) };

        var result = LogPostProcessor.Apply(
            buckets,
            [
                new LogPostProcessFunction { Type = LogPostProcessFunctionType.Absolute },
                new LogPostProcessFunction { Type = LogPostProcessFunctionType.ClampMax, Value = 5 },
            ]);

        Assert.Equal(5, result[0].Count);
    }

    [Fact]
    public void Apply_GroupedBuckets_ProcessesEachGroupIndependently()
    {
        // Interleaved groups, same shape LogQueryService.AggregateAsync's GROUP BY read
        // actually returns. CumulativeSum must run its own running total per GroupKey, not
        // across the flat list, and the reassembled order must match the input order.
        var buckets = new[]
        {
            Bucket(0, 1, "a"),
            Bucket(0, 10, "b"),
            Bucket(1, 2, "a"),
            Bucket(1, 20, "b"),
        };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.CumulativeSum }]);

        Assert.Equal(["a", "b", "a", "b"], result.Select(b => b.GroupKey));
        Assert.Equal([1, 10, 3, 30], result.Select(b => b.Count));
    }

    [Fact]
    public void Apply_UngroupedBuckets_AllShareOneSeries()
    {
        var buckets = new[] { Bucket(0, 1), Bucket(1, 2), Bucket(2, 3) };

        var result = LogPostProcessor.Apply(buckets, [new LogPostProcessFunction { Type = LogPostProcessFunctionType.CumulativeSum }]);

        Assert.Equal([1, 3, 6], result.Select(b => b.Count));
    }
}
