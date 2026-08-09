using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// Covers the one piece of pure, easily-unit-testable logic in the alerting feature -
/// shared by both <c>AlertEvaluationWorker</c>'s real evaluation loop and the
/// <c>/api/alerts/*/test</c> dry-run endpoints (see <see cref="AlertThreshold.IsBreached"/>'s
/// own doc comment). Everything else alerting-related either holds an
/// <see cref="ClickHouse.Driver.IClickHouseClient"/> (<c>AlertQueryService</c>, not
/// unit-tested against a fake - same reasoning <c>LogQueryService</c>'s own tests skip
/// it) or is a thin endpoint/worker shell covered by the live end-to-end verification
/// described in this feature's rollout, not a unit test.
/// </summary>
public class AlertThresholdTests
{
    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, true)]
    [InlineData(3, 2, false)]
    [InlineData(0, 0, true)]
    public void IsBreached_GreaterThanOrEqual(ulong count, ulong observed, bool expected)
    {
        var threshold = new AlertThreshold { Count = count, Comparator = ThresholdComparator.GreaterThanOrEqual };

        Assert.Equal(expected, threshold.IsBreached(observed));
    }

    [Theory]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    [InlineData(3, 4, false)]
    [InlineData(0, 0, false)]
    public void IsBreached_LessThan(ulong count, ulong observed, bool expected)
    {
        var threshold = new AlertThreshold { Count = count, Comparator = ThresholdComparator.LessThan };

        Assert.Equal(expected, threshold.IsBreached(observed));
    }

    [Fact]
    public void IsBreached_DefaultComparatorIsGreaterThanOrEqual()
    {
        var threshold = new AlertThreshold { Count = 5 };

        Assert.True(threshold.IsBreached(5));
        Assert.False(threshold.IsBreached(4));
    }
}
