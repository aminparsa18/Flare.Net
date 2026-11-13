using Flare.Ingest.Otlp;
using Xunit;

namespace Flare.Ingest.Tests;

public class TraceStateThresholdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vendor=value")]
    [InlineData("ot=rv:abcdef01234567")] // ot entry without a threshold
    [InlineData("ot=th:")]
    [InlineData("ot=th:123456789abcdef")] // 15 digits: over the 56-bit limit
    [InlineData("ot=th:zz")]
    [InlineData("xot=th:8")] // a different key that merely ends in "ot"
    public void AdjustedCount_IsOne_WithoutAValidThreshold(string? traceState)
    {
        Assert.Equal(1u, TraceStateThreshold.AdjustedCount(traceState));
    }

    [Theory]
    [InlineData("ot=th:0", 1u)] // probability 1
    [InlineData("ot=th:8", 2u)] // 1/2
    [InlineData("ot=th:c", 4u)] // 1/4
    [InlineData("ot=th:e668", 10u)] // ~1/10, as the spec's 4-digit precision writes it
    [InlineData("ot=th:fd70a4", 100u)] // ~1/100
    [InlineData("ot=th:aaab", 3u)] // ~1/3
    [InlineData("ot=th:6", 2u)] // 62.5%: 1.6 rounds to the nearest whole count
    [InlineData("ot=th:f", 16u)] // one digit, 13 trailing zeros restored
    [InlineData("ot=th:fffffffffffff", uint.MaxValue)] // 2^52 clamps to UInt32
    public void AdjustedCount_DecodesTheThreshold(string traceState, uint expected)
    {
        Assert.Equal(expected, TraceStateThreshold.AdjustedCount(traceState));
    }

    [Theory]
    [InlineData("ot=rv:abcdef01234567;th:c")]
    [InlineData("vendor=x,ot=th:c")]
    [InlineData("vendor=x , \tot=th:c;rv:abcdef01234567 ,other=y")]
    [InlineData("ot=th:C")]
    public void AdjustedCount_FindsThresholdAmongOtherEntries(string traceState)
    {
        Assert.Equal(4u, TraceStateThreshold.AdjustedCount(traceState));
    }

    [Fact]
    public void AdjustedCount_UsesOnlyTheFirstOtMember()
    {
        Assert.Equal(1u, TraceStateThreshold.AdjustedCount("ot=rv:abcdef01234567,ot=th:c"));
    }

    [Fact]
    public void FromThreshold_ClampsToUInt32()
    {
        Assert.Equal(uint.MaxValue, TraceStateThreshold.FromThreshold((1UL << 56) - 1));
    }
}
