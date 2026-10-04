using Flare.Api.Slos;
using Xunit;

namespace Flare.Api.Tests.Slos;

public class SloCalculatorTests
{
    [Fact]
    public void AllowedBadFraction_IsTheComplementOfTheTarget() =>
        Assert.Equal(0.005, SloCalculator.AllowedBadFraction(99.5), 10);

    [Fact]
    public void Sli_IsPercentOfGoodEvents() =>
        Assert.Equal(99.0, SloCalculator.Sli(total: 1000, bad: 10));

    [Fact]
    public void NoEvents_HaveNoSliBurnRateOrBudget()
    {
        Assert.Null(SloCalculator.Sli(0, 0));
        Assert.Null(SloCalculator.BurnRate(0, 0, 99.9));
        Assert.Null(SloCalculator.BudgetRemaining(0, 0, 99.9));
    }

    [Fact]
    public void BurnRate_OfOneSpendsExactlyTheBudget()
    {
        // 99.9% target allows 1 bad per 1000.
        Assert.Equal(1.0, SloCalculator.BurnRate(total: 10_000, bad: 10, targetPercent: 99.9)!.Value, 10);
    }

    [Fact]
    public void BurnRate_ScalesWithTheBadFraction()
    {
        // 1.44% bad against a 0.1% budget is the classic 14.4x fast burn.
        Assert.Equal(14.4, SloCalculator.BurnRate(total: 10_000, bad: 144, targetPercent: 99.9)!.Value, 6);
    }

    [Fact]
    public void BudgetRemaining_IsOneWhenUntouched_ZeroWhenSpent_NegativeWhenOverspent()
    {
        Assert.Equal(1.0, SloCalculator.BudgetRemaining(10_000, 0, 99.9)!.Value, 10);
        Assert.Equal(0.0, SloCalculator.BudgetRemaining(10_000, 10, 99.9)!.Value, 10);
        Assert.Equal(-1.0, SloCalculator.BudgetRemaining(10_000, 20, 99.9)!.Value, 10);
    }

    [Theory]
    [InlineData(14.4, 14.4, 14.4, true)]
    [InlineData(20.0, 5.0, 14.4, false)] // long window burning, but it has already stopped
    [InlineData(5.0, 20.0, 14.4, false)] // brief spike, not sustained
    [InlineData(null, 20.0, 14.4, false)] // no events in the long window
    [InlineData(20.0, null, 14.4, false)]
    public void IsBurning_NeedsBothWindowsAtOrAboveTheThreshold(double? longRate, double? shortRate, double threshold, bool expected) =>
        Assert.Equal(expected, SloCalculator.IsBurning(longRate, shortRate, threshold));
}
