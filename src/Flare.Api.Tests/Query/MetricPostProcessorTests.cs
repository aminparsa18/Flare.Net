using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricPostProcessorTests
{
    private static readonly DateTimeOffset Bucket0 = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    private static MetricSeriesPoint Point(int minute, double? value) =>
        new() { BucketStart = Bucket0.AddMinutes(minute), Value = value };

    [Fact]
    public void Apply_NoFunctions_ReturnsSameReference()
    {
        var points = new[] { Point(0, 1) };

        var result = MetricPostProcessor.Apply(points, []);

        Assert.Same(points, result);
    }

    [Fact]
    public void Apply_ClampMin_RaisesValuesBelowThreshold()
    {
        var points = new[] { Point(0, -5), Point(1, 3), Point(2, null) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.ClampMin, Value = 0 }]);

        Assert.Equal([0, 3, null], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_ClampMax_LowersValuesAboveThreshold()
    {
        var points = new[] { Point(0, 150), Point(1, 3) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.ClampMax, Value = 100 }]);

        Assert.Equal([100, 3], result.Select(p => p.Value));
    }

    [Theory]
    [InlineData(MetricPostProcessFunctionType.ClampMin)]
    [InlineData(MetricPostProcessFunctionType.ClampMax)]
    public void Apply_ClampWithoutValue_Throws(MetricPostProcessFunctionType type)
    {
        var points = new[] { Point(0, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = type, Value = null }]));
    }

    [Fact]
    public void Apply_Absolute_NegatesNegativeValues()
    {
        var points = new[] { Point(0, -4.5), Point(1, 4.5), Point(2, null) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.Absolute }]);

        Assert.Equal([4.5, 4.5, null], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_Log2_ComputesBinaryLog()
    {
        var points = new[] { Point(0, 8) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.Log2 }]);

        Assert.Equal(3, result[0].Value);
    }

    [Fact]
    public void Apply_Log10_ComputesDecimalLog()
    {
        var points = new[] { Point(0, 1000) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.Log10 }]);

        Assert.Equal(3, result[0].Value);
    }

    [Theory]
    [InlineData(MetricPostProcessFunctionType.Log2)]
    [InlineData(MetricPostProcessFunctionType.Log10)]
    public void Apply_LogOfNonPositive_ReturnsNullNotNaN(MetricPostProcessFunctionType type)
    {
        var points = new[] { Point(0, 0d), Point(1, -1) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = type }]);

        Assert.All(result, p => Assert.Null(p.Value));
    }

    [Fact]
    public void Apply_CumulativeSum_AccumulatesRunningTotal()
    {
        var points = new[] { Point(0, 1), Point(1, 2), Point(2, 3) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.CumulativeSum }]);

        Assert.Equal([1, 3, 6], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_CumulativeSum_TreatsGapAsNoIncrement()
    {
        var points = new[] { Point(0, 5), Point(1, null), Point(2, 2) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.CumulativeSum }]);

        Assert.Equal([5, 5, 7], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_PreservesBucketStart()
    {
        var points = new[] { Point(0, 1), Point(5, 2) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.Absolute }]);

        Assert.Equal([Bucket0, Bucket0.AddMinutes(5)], result.Select(p => p.BucketStart));
    }

    [Fact]
    public void Apply_EwmaSmoothing_WeightsRecentPointsMoreHeavily()
    {
        // windowSize=3 -> alpha = 2/(3+1) = 0.5. ewma[0]=10, ewma[1]=0.5*20+0.5*10=15,
        // ewma[2]=0.5*30+0.5*15=22.5.
        var points = new[] { Point(0, 10), Point(1, 20), Point(2, 30) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.EwmaSmoothing, WindowSize = 3 }]);

        Assert.Equal([10, 15, 22.5], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_EwmaSmoothing_CarriesLastAverageForwardOverGap()
    {
        var points = new[] { Point(0, 10), Point(1, null), Point(2, null) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.EwmaSmoothing, WindowSize = 3 }]);

        Assert.Equal([10, 10, 10], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_EwmaSmoothing_NullUntilFirstRealValue()
    {
        var points = new[] { Point(0, null), Point(1, 5) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.EwmaSmoothing, WindowSize = 3 }]);

        Assert.Equal([null, 5], result.Select(p => p.Value));
    }

    [Theory]
    [InlineData(MetricPostProcessFunctionType.EwmaSmoothing)]
    [InlineData(MetricPostProcessFunctionType.MedianSmoothing)]
    public void Apply_SmoothingWithoutWindowSize_Throws(MetricPostProcessFunctionType type)
    {
        var points = new[] { Point(0, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = type, WindowSize = null }]));
    }

    [Theory]
    [InlineData(MetricPostProcessFunctionType.EwmaSmoothing)]
    [InlineData(MetricPostProcessFunctionType.MedianSmoothing)]
    public void Apply_SmoothingWithNonPositiveWindowSize_Throws(MetricPostProcessFunctionType type)
    {
        var points = new[] { Point(0, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = type, WindowSize = 0 }]));
    }

    [Fact]
    public void Apply_MedianSmoothing_ReturnsMedianOfTrailingWindow()
    {
        // window=3, trailing: [1] -> 1; [1,5] -> 3; [1,5,3] -> 3; [5,3,9] -> 5.
        var points = new[] { Point(0, 1), Point(1, 5), Point(2, 3), Point(3, 9) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.MedianSmoothing, WindowSize = 3 }]);

        Assert.Equal([1, 3, 3, 5], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_MedianSmoothing_SkipsNullsWithinWindowInsteadOfBreakingIt()
    {
        // window=3: [10] -> 10; [10,null] -> 10 (null excluded, not counted); [10,null,20] -> 15.
        var points = new[] { Point(0, 10), Point(1, null), Point(2, 20) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.MedianSmoothing, WindowSize = 3 }]);

        Assert.Equal([10, 10, 15], result.Select(p => p.Value));
    }

    [Fact]
    public void Apply_MedianSmoothing_NullWhenWindowHasNoData()
    {
        var points = new[] { Point(0, null), Point(1, null) };

        var result = MetricPostProcessor.Apply(points, [new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.MedianSmoothing, WindowSize = 3 }]);

        Assert.All(result, p => Assert.Null(p.Value));
    }

    [Fact]
    public void Apply_ChainsFunctionsInOrder()
    {
        // abs(-9) = 9, then clampMax(9, 5) = 5 - order matters: clampMax first would give
        // clampMax(-9, 5) = -9, then abs(-9) = 9, a different result.
        var points = new[] { Point(0, -9) };

        var result = MetricPostProcessor.Apply(
            points,
            [
                new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.Absolute },
                new MetricPostProcessFunction { Type = MetricPostProcessFunctionType.ClampMax, Value = 5 },
            ]);

        Assert.Equal(5, result[0].Value);
    }
}
