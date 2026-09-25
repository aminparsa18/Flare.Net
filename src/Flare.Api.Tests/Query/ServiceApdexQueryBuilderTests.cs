using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceApdexQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNoOverrides_UsesPlainDefaultThresholdParameter()
    {
        var result = ServiceApdexQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, new Dictionary<string, int>());

        Assert.Contains(
            "countIf(StatusCode != {errorStatus:String} AND DurationNano <= {apdexDefaultThresholdNano:UInt64}) AS ApdexSatisfiedCount",
            result.Sql);
        Assert.Contains(
            "countIf(StatusCode != {errorStatus:String} AND DurationNano > {apdexDefaultThresholdNano:UInt64} AND DurationNano <= {apdexDefaultThresholdNano:UInt64} * 4) AS ApdexToleratingCount",
            result.Sql);
        Assert.DoesNotContain("multiIf", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(ServiceApdexQueryBuilder.DefaultThresholdMs * 1_000_000UL, parameters["apdexDefaultThresholdNano"]);
    }

    [Fact]
    public void Build_WithOverrides_BuildsMultiIfExpression_AndBindsEachServiceAndThresholdAsParameters()
    {
        var overrides = new Dictionary<string, int> { ["checkout-api"] = 250, ["search-api"] = 1000 };

        var result = ServiceApdexQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, overrides);

        Assert.Contains("multiIf(", result.Sql);
        Assert.Contains("ServiceName = {apdexSvc0:String}, {apdexThreshold0:UInt64}", result.Sql);
        Assert.Contains("ServiceName = {apdexSvc1:String}, {apdexThreshold1:UInt64}", result.Sql);
        Assert.Contains("{apdexDefaultThresholdNano:UInt64})", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        var boundServiceNames = new[] { parameters["apdexSvc0"], parameters["apdexSvc1"] };
        Assert.Contains("checkout-api", boundServiceNames);
        Assert.Contains("search-api", boundServiceNames);
        var boundThresholds = new[] { (ulong)parameters["apdexThreshold0"]!, (ulong)parameters["apdexThreshold1"]! };
        Assert.Contains(250UL * 1_000_000UL, boundThresholds);
        Assert.Contains(1000UL * 1_000_000UL, boundThresholds);
    }

    [Fact]
    public void Build_ExcludesErroredSpansFromSatisfiedAndTolerating_SoTheyCountAsFrustrated()
    {
        var overrides = new Dictionary<string, int> { ["checkout-api"] = 250 };

        var result = ServiceApdexQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, overrides);

        Assert.Contains("countIf(StatusCode != {errorStatus:String} AND DurationNano <= multiIf(", result.Sql);
        Assert.Contains("countIf(StatusCode != {errorStatus:String} AND DurationNano > multiIf(", result.Sql);
        Assert.Equal("STATUS_CODE_ERROR", result.Parameters.ToDictionary()["errorStatus"]);
    }

    [Fact]
    public void Build_FiltersToRootSpansOnly_AndBindsWindow()
    {
        var result = ServiceApdexQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, new Dictionary<string, int>());

        Assert.Contains("ParentSpanId = ''", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
    }

    [Fact]
    public void Build_WithResourceAttributes_AndsInEqualityClauses()
    {
        var resourceAttributes = new[]
        {
            new ResourceAttributeFilter { Key = "deployment.environment", Value = "production" },
        };

        var result = ServiceApdexQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, new Dictionary<string, int>(), resourceAttributes);

        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.Sql);
    }
}
