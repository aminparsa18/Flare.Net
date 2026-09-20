using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceDependencyMetricsQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Build_SelectsMergedNodeStats_FromServiceDependencyNodes_GroupedByService_OrderedBySpanCount()
    {
        var result = ServiceDependencyMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("SELECT", result.Sql);
        Assert.Contains("Service,", result.Sql);
        Assert.Contains("sum(SpanCount) AS SpanCount", result.Sql);
        Assert.Contains("sum(ErrorCount) AS ErrorCount", result.Sql);
        Assert.Contains("sum(TotalDurationNano) AS TotalDurationNano", result.Sql);
        Assert.Contains("topKMerge(3)(TopOperationsState) AS TopOperations", result.Sql);
        Assert.Contains("FROM service_dependency_nodes", result.Sql);
        Assert.Contains("GROUP BY Service", result.Sql);
        Assert.Contains("ORDER BY SpanCount DESC", result.Sql);
    }

    [Fact]
    public void Build_FiltersOnTimeBucket()
    {
        var result = ServiceDependencyMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("WHERE TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}", result.Sql);
    }

    [Fact]
    public void Build_FloorsFromAndCeilsTo_ToWholeMinutes()
    {
        var nonAligned = new DateTimeOffset(2026, 9, 20, 12, 30, 17, TimeSpan.Zero);

        var result = ServiceDependencyMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), nonAligned);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new DateTime(2026, 9, 20, 12, 15, 0, DateTimeKind.Utc), parameters["from"]);
        Assert.Equal(new DateTime(2026, 9, 20, 12, 31, 0, DateTimeKind.Utc), parameters["to"]);
    }

    [Fact]
    public void Build_WithMinuteAlignedNow_DoesNotRoundUpUnnecessarily()
    {
        var result = ServiceDependencyMetricsQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(new DateTime(2026, 9, 20, 12, 15, 0, DateTimeKind.Utc), parameters["from"]);
        Assert.Equal(new DateTime(2026, 9, 20, 12, 30, 0, DateTimeKind.Utc), parameters["to"]);
    }
}
