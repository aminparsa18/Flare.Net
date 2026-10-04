using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class SloRequestTests
{
    private static SloRequest Valid() => new() { Name = "Checkout availability", ServiceName = "shop", TargetPercent = 99.5 };

    [Fact]
    public void Availability_WithDefaults_IsValid() => Assert.Null(Valid().Validate());

    [Fact]
    public void Latency_OnALadderRung_IsValid() =>
        Assert.Null((Valid() with { Kind = SloKind.Latency, LatencyThresholdMs = 500 }).Validate());

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(300)]
    public void Latency_WithoutALadderThreshold_IsInvalid(int? thresholdMs) =>
        Assert.NotNull((Valid() with { Kind = SloKind.Latency, LatencyThresholdMs = thresholdMs }).Validate());

    [Fact]
    public void Availability_WithALatencyThreshold_IsInvalid() =>
        Assert.NotNull((Valid() with { LatencyThresholdMs = 500 }).Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    public void Target_OutsideTheRange_IsInvalid(double target) =>
        Assert.NotNull((Valid() with { TargetPercent = target }).Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void WindowDays_OutsideTheRange_IsInvalid(int days) =>
        Assert.NotNull((Valid() with { WindowDays = days }).Validate());

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Name_AndService_AreRequired(string blank)
    {
        Assert.NotNull((Valid() with { Name = blank }).Validate());
        Assert.NotNull((Valid() with { ServiceName = blank }).Validate());
    }
}
