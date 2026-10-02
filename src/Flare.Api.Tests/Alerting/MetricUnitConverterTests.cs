using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

public class MetricUnitConverterTests
{
    [Theory]
    [InlineData(500, "ms", "s", 0.5)]
    [InlineData(2, "s", "ms", 2000)]
    [InlineData(1, "min", "s", 60)]
    [InlineData(1, "MiBy", "By", 1048576)]
    [InlineData(1, "GBy", "MBy", 1000)]
    [InlineData(5, "ms", "ms", 5)]
    public void TryConvert_SameFamily_Scales(double value, string from, string to, double expected)
    {
        Assert.True(MetricUnitConverter.TryConvert(value, from, to, out var result));
        Assert.Equal(expected, result, 9);
    }

    [Theory]
    [InlineData("ms", "By")]
    [InlineData("ms", "")]
    [InlineData("ms", null)]
    [InlineData("ms", "{request}")]
    [InlineData("Cel", "ms")]
    public void TryConvert_DifferentFamilyOrUnknown_ReturnsFalseAndLeavesValue(string from, string? to)
    {
        Assert.False(MetricUnitConverter.TryConvert(7, from, to, out var result));
        Assert.Equal(7, result);
    }

    [Fact]
    public void ToSeriesUnit_NoThresholdUnit_IsUnchanged() =>
        Assert.Equal(0.5, MetricUnitConverter.ToSeriesUnit(0.5, "", "s"));

    [Fact]
    public void ToSeriesUnit_Convertible_Converts() =>
        Assert.Equal(0.5, MetricUnitConverter.ToSeriesUnit(500, "ms", "s"), 9);

    [Fact]
    public void ToSeriesUnit_IncompatibleSeriesUnit_IsUnchanged() =>
        Assert.Equal(500, MetricUnitConverter.ToSeriesUnit(500, "ms", "By"));
}
