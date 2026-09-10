using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceOverviewQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_SelectsRedMetrics_GroupedByService_OrderedByRequestCount()
    {
        var result = ServiceOverviewQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("SELECT", result.Sql);
        Assert.Contains("ServiceName", result.Sql);
        Assert.Contains("count() AS RequestCount", result.Sql);
        Assert.Contains("countIf(StatusCode = {errorStatus:String}) AS ErrorCount", result.Sql);
        Assert.Contains("quantile(0.5)(DurationNano) AS P50DurationNano", result.Sql);
        Assert.Contains("quantile(0.95)(DurationNano) AS P95DurationNano", result.Sql);
        Assert.Contains("quantile(0.99)(DurationNano) AS P99DurationNano", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("GROUP BY ServiceName", result.Sql);
        Assert.Contains("ORDER BY RequestCount DESC", result.Sql);
    }

    [Fact]
    public void Build_FiltersToRootSpansOnly()
    {
        var result = ServiceOverviewQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("ParentSpanId = ''", result.Sql);
    }

    [Fact]
    public void Build_BindsFromAsNowMinusWindow_AndToAsNow()
    {
        var result = ServiceOverviewQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
        Assert.Equal("STATUS_CODE_ERROR", parameters["errorStatus"]);
    }

    [Theory]
    [InlineData(0, ServiceOverviewQueryBuilder.DefaultWindowMinutes)]
    [InlineData(-5, ServiceOverviewQueryBuilder.DefaultWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(1, 1)]
    [InlineData(5000, ServiceOverviewQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps(int requested, int expected)
    {
        Assert.Equal(expected, ServiceOverviewQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void Build_WithNoResourceAttributes_OmitsAttributeClauses()
    {
        var result = ServiceOverviewQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.DoesNotContain("ResourceAttributes", result.Sql);
    }

    [Fact]
    public void Build_WithResourceAttributes_AndsInEqualityClauses_AndBindsKeyValueParameters()
    {
        var resourceAttributes = new[]
        {
            new ResourceAttributeFilter { Key = "deployment.environment", Value = "production" },
            new ResourceAttributeFilter { Key = "host.name", Value = "web-1" },
        };

        var result = ServiceOverviewQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, resourceAttributes);

        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.Sql);
        Assert.Contains("ResourceAttributes[{ResAttrKey1:String}] = {ResAttrValue1:String}", result.Sql);
        Assert.Contains("WHERE StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)} AND ParentSpanId = '' AND ResourceAttributes", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("deployment.environment", parameters["ResAttrKey0"]);
        Assert.Equal("production", parameters["ResAttrValue0"]);
        Assert.Equal("host.name", parameters["ResAttrKey1"]);
        Assert.Equal("web-1", parameters["ResAttrValue1"]);
    }
}
