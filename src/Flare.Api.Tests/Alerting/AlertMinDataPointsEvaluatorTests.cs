using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers <see cref="AlertMinDataPointsEvaluator.IsInsufficient"/>'s threshold boundary and disabled/not-counted cases.</summary>
public class AlertMinDataPointsEvaluatorTests
{
    [Theory]
    [InlineData(3, 0UL, true)]
    [InlineData(3, 2UL, true)]
    [InlineData(3, 3UL, false)]
    [InlineData(3, 10UL, false)]
    [InlineData(1, 0UL, true)]
    public void Enabled_InsufficientOnlyBelowTheMinimum(int minDataPoints, ulong pointCount, bool expected)
    {
        Assert.Equal(expected, AlertMinDataPointsEvaluator.IsInsufficient(minDataPoints, pointCount));
    }

    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(0, null)]
    [InlineData(3, null)]
    public void DisabledOrNotCounted_IsNeverInsufficient(int minDataPoints, ulong? pointCount)
    {
        Assert.False(AlertMinDataPointsEvaluator.IsInsufficient(minDataPoints, pointCount));
    }
}
