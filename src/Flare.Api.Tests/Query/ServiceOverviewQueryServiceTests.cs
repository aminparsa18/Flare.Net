using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

/// <summary>
/// Covers <see cref="ServiceOverviewQueryService.BuildMetrics"/> - the pure, ClickHouse-free
/// row-shaping method - directly against hand-built values, same style as
/// <c>IngestionStatsQueryServiceTests</c> covering <c>BuildBuckets</c>.
/// <see cref="ServiceOverviewQueryService"/> itself holds an <c>IClickHouseClient</c> and is
/// deliberately not unit-tested against a fake - see the repo's CLAUDE.md testing note and
/// <c>SpanQueryService</c>'s own precedent.
/// </summary>
public class ServiceOverviewQueryServiceTests
{
    [Fact]
    public void BuildMetrics_ComputesErrorRateAndRequestsPerSecond()
    {
        var result = ServiceOverviewQueryService.BuildMetrics(
            serviceName: "checkout-api",
            requestCount: 900,
            errorCount: 90,
            p50DurationNano: 5_000_000,
            p95DurationNano: 20_000_000,
            p99DurationNano: 50_000_000,
            window: TimeSpan.FromMinutes(15));

        Assert.Equal("checkout-api", result.ServiceName);
        Assert.Equal(900UL, result.RequestCount);
        Assert.Equal(90UL, result.ErrorCount);
        Assert.Equal(0.1, result.ErrorRate, precision: 10);
        Assert.Equal(1.0, result.RequestsPerSecond, precision: 10); // 900 requests / 900 seconds
    }

    [Fact]
    public void BuildMetrics_ConvertsDurationNanosToMilliseconds()
    {
        var result = ServiceOverviewQueryService.BuildMetrics(
            serviceName: "checkout-api",
            requestCount: 10,
            errorCount: 0,
            p50DurationNano: 1_000_000,
            p95DurationNano: 2_500_000,
            p99DurationNano: 9_999_000,
            window: TimeSpan.FromMinutes(1));

        Assert.Equal(1.0, result.P50DurationMs, precision: 10);
        Assert.Equal(2.5, result.P95DurationMs, precision: 10);
        Assert.Equal(9.999, result.P99DurationMs, precision: 10);
    }

    [Fact]
    public void BuildMetrics_ZeroErrors_YieldsZeroErrorRate()
    {
        var result = ServiceOverviewQueryService.BuildMetrics(
            serviceName: "checkout-api",
            requestCount: 500,
            errorCount: 0,
            p50DurationNano: 0,
            p95DurationNano: 0,
            p99DurationNano: 0,
            window: TimeSpan.FromMinutes(5));

        Assert.Equal(0.0, result.ErrorRate);
    }

    [Fact]
    public void BuildMetrics_AllErrors_YieldsErrorRateOfOne()
    {
        var result = ServiceOverviewQueryService.BuildMetrics(
            serviceName: "checkout-api",
            requestCount: 42,
            errorCount: 42,
            p50DurationNano: 0,
            p95DurationNano: 0,
            p99DurationNano: 0,
            window: TimeSpan.FromMinutes(5));

        Assert.Equal(1.0, result.ErrorRate);
    }
}
