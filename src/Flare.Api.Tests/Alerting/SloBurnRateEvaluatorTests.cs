using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

public class SloBurnRateEvaluatorTests
{
    [Fact]
    public void Decide_BreachesWhenBothWindowsBurnAtTheThreshold()
    {
        var result = SloBurnRateEvaluator.Decide(15, 20, 14.4);

        Assert.True(result.Breached);
        Assert.Equal(15, result.LongBurnRate);
        Assert.Equal(20, result.ShortBurnRate);
    }

    [Fact]
    public void Decide_DoesNotBreachOnceTheShortWindowRecovers() =>
        Assert.False(SloBurnRateEvaluator.Decide(15, 1, 14.4).Breached);

    [Fact]
    public void Decide_DoesNotBreachWithoutTraffic() =>
        Assert.False(SloBurnRateEvaluator.Decide(null, null, 14.4).Breached);
}
