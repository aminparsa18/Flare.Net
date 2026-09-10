using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ServiceDependencyQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_NodesQuery_GroupsByEffectiveService_WithPeerServiceOverride()
    {
        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], ServiceName) AS Service", result.NodesSql);
        Assert.Contains("count() AS SpanCount", result.NodesSql);
        Assert.Contains("countIf(StatusCode = {errorStatus:String}) AS ErrorCount", result.NodesSql);
        Assert.Contains("sum(DurationNano) AS TotalDurationNano", result.NodesSql);
        Assert.Contains("topK(3)(Name) AS TopOperations", result.NodesSql);
        Assert.Contains("FROM spans\n", result.NodesSql);
        Assert.Contains("GROUP BY Service", result.NodesSql);
        Assert.Contains("ORDER BY SpanCount DESC", result.NodesSql);
    }

    [Fact]
    public void Build_EdgesQuery_SelfJoinsOnTraceAndParentSpanId_ExcludingSameServiceEdges()
    {
        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("if(parent.SpanAttributes['peer.service'] != '', parent.SpanAttributes['peer.service'], parent.ServiceName) AS Source", result.EdgesSql);
        Assert.Contains("if(child.SpanAttributes['peer.service'] != '', child.SpanAttributes['peer.service'], child.ServiceName) AS Target", result.EdgesSql);
        Assert.Contains("count() AS CallCount", result.EdgesSql);
        Assert.Contains("sum(child.DurationNano) AS TotalDurationNano", result.EdgesSql);
        Assert.Contains("INNER JOIN spans AS parent ON parent.TraceId = child.TraceId AND parent.SpanId = child.ParentSpanId", result.EdgesSql);
        Assert.Contains("child.ParentSpanId != ''", result.EdgesSql);
        Assert.Contains("HAVING Source != Target", result.EdgesSql);
        Assert.Contains("ORDER BY CallCount DESC", result.EdgesSql);
    }

    [Fact]
    public void Build_BindsFromAsNowMinusWindow_AndToAsNow_ForBothQueries()
    {
        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        var nodesParameters = result.NodesParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, nodesParameters["from"]);
        Assert.Equal(Now.UtcDateTime, nodesParameters["to"]);
        Assert.Equal("STATUS_CODE_ERROR", nodesParameters["errorStatus"]);

        var edgesParameters = result.EdgesParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, edgesParameters["from"]);
        Assert.Equal(Now.UtcDateTime, edgesParameters["to"]);
    }

    [Theory]
    [InlineData(0, ServiceDependencyQueryBuilder.DefaultWindowMinutes)]
    [InlineData(-5, ServiceDependencyQueryBuilder.DefaultWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(1, 1)]
    [InlineData(5000, ServiceDependencyQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps_SameAsServiceOverviewQueryBuilder(int requested, int expected)
    {
        Assert.Equal(expected, ServiceDependencyQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void Build_WithNoResourceAttributes_OmitsAttributeClauses()
    {
        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.DoesNotContain("ResourceAttributes", result.NodesSql);
        Assert.DoesNotContain("ResourceAttributes", result.EdgesSql);
    }

    [Fact]
    public void Build_WithResourceAttributes_AndsInEqualityClauses_UnqualifiedOnNodes_BothAliasesOnEdges()
    {
        var resourceAttributes = new[] { new ResourceAttributeFilter { Key = "deployment.environment", Value = "production" } };

        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, resourceAttributes);

        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.NodesSql);
        var nodesParameters = result.NodesParameters.ToDictionary();
        Assert.Equal("deployment.environment", nodesParameters["ResAttrKey0"]);
        Assert.Equal("production", nodesParameters["ResAttrValue0"]);

        // Edges query: both the "parent." and "child." sides must match, not just one - see
        // ServiceDependencyQueryBuilder.Build's own remarks on why this differs from the
        // window predicate's documented child-only latitude.
        Assert.Contains("parent.ResourceAttributes[{parentResAttrKey0:String}] = {parentResAttrValue0:String}", result.EdgesSql);
        Assert.Contains("child.ResourceAttributes[{childResAttrKey0:String}] = {childResAttrValue0:String}", result.EdgesSql);
        var edgesParameters = result.EdgesParameters.ToDictionary();
        Assert.Equal("deployment.environment", edgesParameters["parentResAttrKey0"]);
        Assert.Equal("production", edgesParameters["parentResAttrValue0"]);
        Assert.Equal("deployment.environment", edgesParameters["childResAttrKey0"]);
        Assert.Equal("production", edgesParameters["childResAttrValue0"]);
    }
}
