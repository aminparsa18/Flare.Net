using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ApdexScoreCalculatorTests
{
    [Fact]
    public void Calculate_AllSatisfied_YieldsOne()
    {
        Assert.Equal(1.0, ApdexScoreCalculator.Calculate(satisfiedCount: 100, toleratingCount: 0, requestCount: 100));
    }

    [Fact]
    public void Calculate_AllFrustrated_YieldsZero()
    {
        Assert.Equal(0.0, ApdexScoreCalculator.Calculate(satisfiedCount: 0, toleratingCount: 0, requestCount: 100));
    }

    [Fact]
    public void Calculate_MixOfBuckets_WeightsToleratingAsHalf()
    {
        // 80 satisfied + 20 tolerating (counted as half) + 0 implied frustrated, of 100 total.
        var score = ApdexScoreCalculator.Calculate(satisfiedCount: 80, toleratingCount: 20, requestCount: 100);
        Assert.NotNull(score);
        Assert.Equal(0.9, score.Value, precision: 10);
    }

    [Fact]
    public void Calculate_ZeroRequests_YieldsNull()
    {
        Assert.Null(ApdexScoreCalculator.Calculate(satisfiedCount: 0, toleratingCount: 0, requestCount: 0));
    }
}
