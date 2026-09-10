using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceCallBreakdownQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ExternalCallsQuery_GroupsByPeerService_FilteredToRequestedService()
    {
        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        Assert.Contains("SpanAttributes['peer.service'] AS PeerService", result.ExternalCallsSql);
        Assert.Contains("count() AS CallCount", result.ExternalCallsSql);
        Assert.Contains("countIf(StatusCode = {errorStatus:String}) AS ErrorCount", result.ExternalCallsSql);
        Assert.Contains("quantile(0.5)(DurationNano) AS P50DurationNano", result.ExternalCallsSql);
        Assert.Contains("quantile(0.95)(DurationNano) AS P95DurationNano", result.ExternalCallsSql);
        Assert.Contains("WHERE ServiceName = {service:String} AND SpanAttributes['peer.service'] != ''", result.ExternalCallsSql);
        Assert.Contains("GROUP BY PeerService", result.ExternalCallsSql);
        Assert.Contains("ORDER BY CallCount DESC", result.ExternalCallsSql);

        var parameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal("checkout-api", parameters["service"]);
    }

    [Fact]
    public void Build_DatabaseCallsQuery_GroupsByDbSystemAndOperation_FilteredToRequestedService()
    {
        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        Assert.Contains("SpanAttributes['db.system'] AS DbSystem", result.DatabaseCallsSql);
        Assert.Contains("SpanAttributes['db.operation'] AS DbOperation", result.DatabaseCallsSql);
        Assert.Contains("WHERE ServiceName = {service:String} AND SpanAttributes['db.system'] != ''", result.DatabaseCallsSql);
        Assert.Contains("GROUP BY DbSystem, DbOperation", result.DatabaseCallsSql);
        Assert.Contains("ORDER BY CallCount DESC", result.DatabaseCallsSql);

        var parameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal("checkout-api", parameters["service"]);
    }

    [Fact]
    public void Build_BindsFromAsNowMinusWindow_AndToAsNow_ForBothQueries()
    {
        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now);

        var externalParameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, externalParameters["from"]);
        Assert.Equal(Now.UtcDateTime, externalParameters["to"]);
        Assert.Equal("STATUS_CODE_ERROR", externalParameters["errorStatus"]);

        var databaseParameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, databaseParameters["from"]);
        Assert.Equal(Now.UtcDateTime, databaseParameters["to"]);
    }

    [Theory]
    [InlineData(0, ServiceCallBreakdownQueryBuilder.DefaultWindowMinutes)]
    [InlineData(-5, ServiceCallBreakdownQueryBuilder.DefaultWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(5000, ServiceCallBreakdownQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps_SameAsServiceOverviewQueryBuilder(int requested, int expected)
    {
        Assert.Equal(expected, ServiceCallBreakdownQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void Build_WithResourceAttributes_AndsInEqualityClauses_OnBothQueries()
    {
        var resourceAttributes = new[] { new ResourceAttributeFilter { Key = "deployment.environment", Value = "production" } };

        var result = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromMinutes(15), Now, resourceAttributes);

        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.ExternalCallsSql);
        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.DatabaseCallsSql);

        var externalParameters = result.ExternalCallsParameters.ToDictionary();
        Assert.Equal("deployment.environment", externalParameters["ResAttrKey0"]);
        Assert.Equal("production", externalParameters["ResAttrValue0"]);

        var databaseParameters = result.DatabaseCallsParameters.ToDictionary();
        Assert.Equal("deployment.environment", databaseParameters["ResAttrKey0"]);
        Assert.Equal("production", databaseParameters["ResAttrValue0"]);
    }
}
