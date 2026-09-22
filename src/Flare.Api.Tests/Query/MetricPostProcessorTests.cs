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
