using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceCallBreakdownMetricsQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ExternalCallsQuery_MergesFromServiceCallBreakdownExternal_FilteredToRequestedService()
    {
        var result = ServiceCallBreakdownMetricsQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        Assert.Contains("sum(CallCount) AS CallCount", result.ExternalCallsSql);
        Assert.Contains("sum(ErrorCount) AS ErrorCount", result.ExternalCallsSql);
        Assert.Contains("quantileMerge(0.5)(P50State) AS P50DurationNano", result.ExternalCallsSql);
        Assert.Contains("quantileMerge(0.95)(P95State) AS P95DurationNano", result.ExternalCallsSql);
        Assert.Contains("FROM service_call_breakdown_external", result.ExternalCallsSql);
        Assert.Contains("WHERE ServiceName = {service:String} AND TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}", result.ExternalCallsSql);
        Assert.Contains("GROUP BY PeerService", result.ExternalCallsSql);
        Assert.Contains("ORDER BY CallCount DESC", result.ExternalCallsSql);

        var parameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal("checkout-api", parameters["service"]);
    }

    [Fact]
    public void Build_DatabaseCallsQuery_MergesFromServiceCallBreakdownDatabase_FilteredToRequestedService()
    {
        var result = ServiceCallBreakdownMetricsQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        Assert.Contains("FROM service_call_breakdown_database", result.DatabaseCallsSql);
        Assert.Contains("WHERE ServiceName = {service:String} AND TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}", result.DatabaseCallsSql);
        Assert.Contains("GROUP BY DbSystem, DbOperation", result.DatabaseCallsSql);
        Assert.Contains("ORDER BY CallCount DESC", result.DatabaseCallsSql);

        var parameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal("checkout-api", parameters["service"]);
    }

    [Fact]
    public void Build_FloorsFromAndCeilsTo_ToWholeMinutes_ForBothQueries()
    {
        var nonAligned = new DateTimeOffset(2026, 9, 20, 12, 30, 17, TimeSpan.Zero);

        var result = ServiceCallBreakdownMetricsQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), nonAligned);

        var externalParameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal(new DateTime(2026, 9, 20, 12, 15, 0, DateTimeKind.Utc), externalParameters["from"]);
        Assert.Equal(new DateTime(2026, 9, 20, 12, 31, 0, DateTimeKind.Utc), externalParameters["to"]);

        var databaseParameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal(new DateTime(2026, 9, 20, 12, 15, 0, DateTimeKind.Utc), databaseParameters["from"]);
        Assert.Equal(new DateTime(2026, 9, 20, 12, 31, 0, DateTimeKind.Utc), databaseParameters["to"]);
    }
}
