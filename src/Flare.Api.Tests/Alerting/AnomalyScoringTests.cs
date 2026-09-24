using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers <see cref="AnomalyScoring"/>'s pure z-score math - see <c>docs-internal/adr/0048-anomaly-detection-alerting.md</c>.</summary>
public class AnomalyScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 15, 0, 0, TimeSpan.Zero);

    private static AnomalyCondition Condition(AnomalyDirection direction = AnomalyDirection.Both, double z = 3, AnomalySeasonality seasonality = AnomalySeasonality.Daily, int periods = 5) => new()
    {
        Direction = direction,
        ZScoreThreshold = z,
        Seasonality = seasonality,
        BaselinePeriods = periods,
    };

    [Fact]
    public void BaselineWindows_Daily_ShiftsTheSameWindowBackOneDayAtATime()
    {
        var windows = AnomalyScoring.BaselineWindows(Condition(periods: 3), TimeSpan.FromMinutes(5), Now);

        Assert.Equal(
            [
                (Now.AddDays(-1).AddMinutes(-5), Now.AddDays(-1)),
                (Now.AddDays(-2).AddMinutes(-5), Now.AddDays(-2)),
                (Now.AddDays(-3).AddMinutes(-5), Now.AddDays(-3)),
            ],
            windows);
    }

    [Fact]
    public void BaselineWindows_Weekly_ShiftsByWeeks()
    {
        var windows = AnomalyScoring.BaselineWindows(Condition(seasonality: AnomalySeasonality.Weekly, periods: 4), TimeSpan.FromMinutes(10), Now);

        Assert.Equal(4, windows.Count);
        Assert.Equal((Now.AddDays(-28).AddMinutes(-10), Now.AddDays(-28)), windows[3]);
    }

    [Fact]
    public void Score_NormalValue_DoesNotBreach()
    {
        var score = AnomalyScoring.Score(100, [90, 100, 110, 100, 100], Condition());

        Assert.False(score.Breached);
        Assert.Equal(100, score.BaselineMean);
        Assert.Equal(0, score.ZScore);
        Assert.Equal(5, score.SampleCount);
    }

    [Fact]
    public void Score_TrafficHalved_BreachesBelow()
    {
        // mean 1000, σ ≈ 44.7 -> z ≈ -11.2
        var score = AnomalyScoring.Score(500, [950, 1050, 1000, 950, 1050], Condition(AnomalyDirection.Below));

        Assert.True(score.Breached);
        Assert.True(score.ZScore < -3);
    }

    [Theory]
    [InlineData(AnomalyDirection.Above, 500, false)]
    [InlineData(AnomalyDirection.Above, 1500, true)]
    [InlineData(AnomalyDirection.Below, 1500, false)]
    [InlineData(AnomalyDirection.Both, 500, true)]
    [InlineData(AnomalyDirection.Both, 1500, true)]
    public void Score_RespectsDirection(AnomalyDirection direction, double current, bool expected)
    {
        var score = AnomalyScoring.Score(current, [950, 1050, 1000, 950, 1050], Condition(direction));

        Assert.Equal(expected, score.Breached);
    }

    [Fact]
    public void Score_ZeroAndNaNBaselineSamples_AreMissing()
    {
        var score = AnomalyScoring.Score(100, [0, double.NaN, 100, 0], Condition());

        Assert.Equal(1, score.SampleCount);
        Assert.Null(score.BaselineMean);
        Assert.Null(score.ZScore);
        Assert.False(score.Breached);
    }

    [Fact]
    public void Score_BelowMinimumHistory_NeverBreaches()
    {
        var score = AnomalyScoring.Score(1_000_000, [100, 100], Condition());

        Assert.Equal(2, score.SampleCount);
        Assert.False(score.Breached);
    }

    [Fact]
    public void Score_CurrentZero_IsKeptAndBreachesBelow()
    {
        var score = AnomalyScoring.Score(0, [100, 110, 90], Condition(AnomalyDirection.Below));

        Assert.True(score.Breached);
    }

    [Fact]
    public void Score_ConstantBaseline_FloorsStdDevAtFivePercentOfMean()
    {
        // σ = 0, so σ_eff = 5 -> 110 is z = 2 (not an anomaly at k = 3), 120 is z = 4.
        var baseline = new double[] { 100, 100, 100 };

        Assert.Equal(2, AnomalyScoring.Score(110, baseline, Condition()).ZScore!.Value, 6);
        Assert.False(AnomalyScoring.Score(110, baseline, Condition()).Breached);
        Assert.True(AnomalyScoring.Score(120, baseline, Condition()).Breached);
    }

    [Fact]
    public void Score_NaNCurrent_NeverBreaches()
    {
        var score = AnomalyScoring.Score(double.NaN, [100, 110, 90], Condition());

        Assert.False(score.Breached);
        Assert.NotNull(score.BaselineMean);
        Assert.Null(score.ZScore);
    }

    [Theory]
    [InlineData(AlertConditionKind.LogCount, AlertConditionKind.LogCount)]
    [InlineData(AlertConditionKind.MetricThreshold, AlertConditionKind.MetricThreshold)]
    [InlineData(AlertConditionKind.Anomaly, AlertConditionKind.ExceptionCount)]
    public void SeriesKind_AnomalyResolvesToItsSource(AlertConditionKind kind, AlertConditionKind expected)
    {
        Assert.Equal(expected, AnomalyScoring.SeriesKind(kind, new AnomalyCondition { Source = AlertConditionKind.ExceptionCount, BaselinePeriods = 7, ZScoreThreshold = 3 }));
    }
}
