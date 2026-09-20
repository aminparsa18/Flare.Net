using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceMetricsQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Build_SelectsMergedRedMetrics_FromServiceMetrics_GroupedByService_OrderedByRequestCount()
    {
        var result = ServiceMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("SELECT", result.Sql);
        Assert.Contains("ServiceName", result.Sql);
        Assert.Contains("sum(RequestCount) AS RequestCount", result.Sql);
        Assert.Contains("sum(ErrorCount) AS ErrorCount", result.Sql);
        Assert.Contains("quantileMerge(0.5)(P50State) AS P50DurationNano", result.Sql);
        Assert.Contains("quantileMerge(0.95)(P95State) AS P95DurationNano", result.Sql);
        Assert.Contains("quantileMerge(0.99)(P99State) AS P99DurationNano", result.Sql);
        Assert.Contains("FROM service_metrics", result.Sql);
        Assert.Contains("GROUP BY ServiceName", result.Sql);
        Assert.Contains("ORDER BY RequestCount DESC", result.Sql);
    }

    [Fact]
    public void Build_FiltersOnTimeBucket()
    {
        var result = ServiceMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("WHERE TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}", result.Sql);
    }

    [Fact]
    public void Build_FloorsFromAndCeilsTo_ToWholeMinutes()
    {
        // Now = 12:30:00 exactly, so `to` needs no rounding; `from` (Now - 15m = 12:15:00)
        // also lands exactly on a minute - use a non-aligned `now` to actually exercise the
        // floor/ceil behavior described in ServiceMetricsQueryBuilder's remarks.
        var nonAligned = new DateTimeOffset(2026, 9, 20, 12, 30, 17, TimeSpan.Zero);

        var result = ServiceMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), nonAligned);

        var parameters = result.Parameters.ToDictionary();
        // from = 12:15:17 floored down to 12:15:00.
        Assert.Equal(new DateTime(2026, 9, 20, 12, 15, 0, DateTimeKind.Utc), parameters["from"]);
        // to = 12:30:17 ceilinged up to 12:31:00.
        Assert.Equal(new DateTime(2026, 9, 20, 12, 31, 0, DateTimeKind.Utc), parameters["to"]);
    }

    [Fact]
    public void Build_WithMinuteAlignedNow_DoesNotRoundUpUnnecessarily()
    {
        var result = ServiceMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new DateTime(2026, 9, 20, 12, 15, 0, DateTimeKind.Utc), parameters["from"]);
        Assert.Equal(new DateTime(2026, 9, 20, 12, 30, 0, DateTimeKind.Utc), parameters["to"]);
    }
}
