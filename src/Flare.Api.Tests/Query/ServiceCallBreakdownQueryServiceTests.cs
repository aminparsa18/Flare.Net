using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

/// <summary>
/// Covers <see cref="ServiceCallBreakdownQueryService.ErrorRate"/> - the pure, ClickHouse-free
/// derived-stat method - directly against hand-built values, same style as
/// <see cref="ServiceOverviewQueryServiceTests"/> covering <c>BuildMetrics</c>.
/// <see cref="ServiceCallBreakdownQueryService"/> itself holds an <c>IClickHouseClient</c> and
/// is deliberately not unit-tested against a fake - see the repo's CLAUDE.md testing note.
/// </summary>
public class ServiceCallBreakdownQueryServiceTests
{
    [Fact]
    public void ErrorRate_ComputesFraction()
    {
        Assert.Equal(0.1, ServiceCallBreakdownQueryService.ErrorRate(errorCount: 9, callCount: 90), precision: 10);
    }

    [Fact]
    public void ErrorRate_ZeroCalls_YieldsZero_NotDivideByZero()
    {
        Assert.Equal(0.0, ServiceCallBreakdownQueryService.ErrorRate(errorCount: 0, callCount: 0));
    }

    [Fact]
    public void ErrorRate_AllErrors_YieldsOne()
    {
        Assert.Equal(1.0, ServiceCallBreakdownQueryService.ErrorRate(errorCount: 42, callCount: 42));
    }

    [Fact]
    public void ErrorRate_ZeroErrors_YieldsZero()
    {
        Assert.Equal(0.0, ServiceCallBreakdownQueryService.ErrorRate(errorCount: 0, callCount: 500));
    }
}
