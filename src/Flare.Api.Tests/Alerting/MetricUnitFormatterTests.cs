using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="MetricUnitFormatter"/>'s unit table against the same cases the
/// dashboard's <c>lib/metrics/axis.ts</c> this was ported from handles - bytes/time scale
/// selection, "%"/dimensionless/rate units, and the unrecognized-unit passthrough.
/// </summary>
public class MetricUnitFormatterTests
{
    [Theory]
    [InlineData("By", 4294967296d, "4 GB")]
    [InlineData("By", 512d, "512 B")]
    [InlineData("GiBy", 4d, "4 GB")]
    public void Format_BytesUnit_ScalesToReadableMagnitude(string unit, double raw, string expected)
    {
        var scale = MetricUnitFormatter.ResolveScale(unit, raw);
        Assert.Equal(expected, MetricUnitFormatter.Format(raw, scale));
    }

    [Fact]
    public void Format_SecondsUnit_SubSecondScalesToMilliseconds()
    {
        var scale = MetricUnitFormatter.ResolveScale("s", 0.03);
        Assert.Equal("30 ms", MetricUnitFormatter.Format(0.03, scale));
    }

    [Fact]
    public void Format_PercentUnit_AppendsPercentSignWithNoSpace()
    {
        var scale = MetricUnitFormatter.ResolveScale("%", 87.5);
        Assert.Equal("87.5%", MetricUnitFormatter.Format(87.5, scale));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    public void Format_DimensionlessUnit_NoSuffix(string? unit)
    {
        var scale = MetricUnitFormatter.ResolveScale(unit, 42);
        Assert.Equal("42", MetricUnitFormatter.Format(42, scale));
    }

    [Fact]
    public void Format_CurlyBraceAnnotation_OutsideARateIsPlainDimensionless()
    {
        // Unlike the rate case below, a bare "{exception}" (no "/denominator") is treated
        // as a plain count - the annotation word only surfaces as a suffix when it's a
        // rate's numerator (see ResolveScale's remarks, mirroring axis.ts exactly).
        var scale = MetricUnitFormatter.ResolveScale("{exception}", 3);
        Assert.Equal("3", MetricUnitFormatter.Format(3, scale));
    }

    [Fact]
    public void Format_RateUnit_SplitsNumeratorAndKeepsDenominatorLiteral()
    {
        var scale = MetricUnitFormatter.ResolveScale("By/s", 2 * 1024d * 1024);
        Assert.Equal("2 MB/s", MetricUnitFormatter.Format(2 * 1024d * 1024, scale));
    }

    [Fact]
    public void Format_UnrecognizedUnit_KeepsItAsLiteralSuffix()
    {
        var scale = MetricUnitFormatter.ResolveScale("Cel", 21.4);
        Assert.Equal("21.4 Cel", MetricUnitFormatter.Format(21.4, scale));
    }

    [Fact]
    public void ResolveScale_SharedPeak_PutsBothValuesOnSameScale()
    {
        // Mirrors AlertMessageFormatter.BuildFiredText's usage: observed and threshold
        // must read in the same unit rather than each picking its own.
        var thresholdBytes = 500d * 1024 * 1024;
        var observedBytes = 1000d * 1024 * 1024;
        var scale = MetricUnitFormatter.ResolveScale("By", Math.Max(observedBytes, thresholdBytes));

        Assert.Equal("500 MB", MetricUnitFormatter.Format(thresholdBytes, scale));
        Assert.Equal("1000 MB", MetricUnitFormatter.Format(observedBytes, scale));
    }
}
