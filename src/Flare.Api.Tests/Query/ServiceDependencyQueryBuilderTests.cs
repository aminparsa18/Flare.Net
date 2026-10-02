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

    [Fact]
    public void Build_EdgesQuery_BoundsParentStartTime_ToWindowWidenedBySlack()
    {
        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        // Without a parent-side bound the join's right side reads the whole table - see the
        // builder's remarks and migration 0025.
        Assert.Contains("parent.StartTime >= {parentFrom:DateTime64(9)} AND parent.StartTime < {to:DateTime64(9)}", result.EdgesSql);
        var edgesParameters = result.EdgesParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).Subtract(ServiceDependencyQueryBuilder.ParentStartSlack).UtcDateTime, edgesParameters["parentFrom"]);
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

    [Fact]
    public void Build_ExternalLeavesQuery_AntiJoinsChildSpans_GroupsByCallerAndDomain()
    {
        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("client.ServiceName AS Source", result.ExternalLeavesSql);
        Assert.Contains($"{ExternalApiQueryBuilder.DomainExpr} AS Target", result.ExternalLeavesSql);
        Assert.Contains(ExternalApiQueryBuilder.OutboundCallCondition, result.ExternalLeavesSql);
        Assert.Contains("client.SpanAttributes['peer.service'] = ''", result.ExternalLeavesSql);
        Assert.Contains("LEFT ANTI JOIN (", result.ExternalLeavesSql);
        Assert.Contains("ON child.TraceId = client.TraceId AND child.ParentSpanId = client.SpanId", result.ExternalLeavesSql);
        Assert.Contains("HAVING Target != ''", result.ExternalLeavesSql);
        Assert.EndsWith($"LIMIT {ServiceDependencyQueryBuilder.MaxExternalLeaves}", result.ExternalLeavesSql);

        var parameters = result.ExternalLeavesParameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-15).UtcDateTime, parameters["from"]);
        Assert.Equal((Now - TimeSpan.FromMinutes(15) - ServiceDependencyQueryBuilder.ChildStartSkew).UtcDateTime, parameters["childFrom"]);
        Assert.Equal((Now + ServiceDependencyQueryBuilder.ChildStartSkew).UtcDateTime, parameters["childTo"]);
    }

    [Fact]
    public void Build_ExternalLeavesQuery_AppliesResourceAttributesToCallerOnly()
    {
        var resourceAttributes = new[] { new ResourceAttributeFilter { Key = "deployment.environment", Value = "production" } };

        var result = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now, resourceAttributes);

        // The child side must stay unfiltered: a callee in another environment still
        // answers the call, so it isn't an external host.
        Assert.Contains("client.ResourceAttributes[{clientResAttrKey0:String}] = {clientResAttrValue0:String}", result.ExternalLeavesSql);
        Assert.DoesNotContain("child.ResourceAttributes", result.ExternalLeavesSql);
    }

    [Fact]
    public void BuildExternalLeavesFromOutboundCalls_ReadsOutboundCallsTable_WithTheSameAntiJoin()
    {
        var live = ServiceDependencyQueryBuilder.Build(TimeSpan.FromMinutes(15), Now);
        var (sql, parameters) = ServiceDependencyQueryBuilder.BuildExternalLeavesFromOutboundCalls(TimeSpan.FromMinutes(15), Now);

        Assert.Contains("client.Domain AS Target", sql);
        Assert.Contains("FROM outbound_calls AS client\n", sql);
        Assert.Contains("WHERE client.StartTime >= {from:DateTime64(9)} AND client.StartTime < {to:DateTime64(9)}", sql);
        var antiJoin = sql[sql.IndexOf("LEFT ANTI JOIN", StringComparison.Ordinal)..sql.IndexOf("WHERE client.", StringComparison.Ordinal)];
        Assert.Contains(antiJoin, live.ExternalLeavesSql);
        Assert.Equal(live.ExternalLeavesParameters.ToDictionary(), parameters.ToDictionary());
    }

    [Theory]
    [InlineData("Flare.ServiceDefaults.ClickHouseMigrations.Sql.0037_db_stable_semconv.sql")]
    [InlineData("Flare.ServiceDefaults.ClickHouseMigrations.SqlCluster.0037_db_stable_semconv.sql")]
    public void Migration0037_FiltersAndKeysOutboundCallsLikeTheLiveQuery(string resourceName)
    {
        // The outbound_calls path and the live path must pick the same calls and domains,
        // or adding a filter chip would change the Map's leaves. 0037 is outbound_calls_mv's
        // latest definition (0035 created it).
        var assembly = typeof(Flare.ServiceDefaults.ClickHouseMigrations.ClickHouseMigrationRunner).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        var sql = new StreamReader(stream).ReadToEnd();

        Assert.Contains($"{ExternalApiQueryBuilder.DomainExpr} AS Domain", sql);
        Assert.Contains($"WHERE {ExternalApiQueryBuilder.OutboundCallCondition} AND SpanAttributes['peer.service'] = '' AND Domain != ''", sql);
    }

    [Fact]
    public void MergeExternalLeaves_AddsOneExternalNodePerHost_SummedAcrossCallers()
    {
        var nodes = new[] { Node("checkout"), Node("billing") };
        var leaves = new[]
        {
            new ServiceDependencyExternalLeaf("checkout", "api.stripe.com", 10, 2, 1000, ["POST", "GET"]),
            new ServiceDependencyExternalLeaf("billing", "api.stripe.com", 5, 1, 500, ["GET", "DELETE"]),
        };

        var (mergedNodes, mergedEdges) = ServiceDependencyQueryBuilder.MergeExternalLeaves(nodes, [], leaves);

        var stripe = Assert.Single(mergedNodes, n => n.IsExternal);
        Assert.Equal("api.stripe.com", stripe.Service);
        Assert.Equal(15UL, stripe.SpanCount);
        Assert.Equal(3UL, stripe.ErrorCount);
        Assert.Equal(1500UL, stripe.TotalDurationNano);
        Assert.Equal(["POST", "GET", "DELETE"], stripe.TopOperations);
        Assert.Equal(2, mergedEdges.Count);
        Assert.Contains(mergedEdges, e => e is { Source: "checkout", Target: "api.stripe.com", CallCount: 10 });
        Assert.Contains(mergedEdges, e => e is { Source: "billing", Target: "api.stripe.com", CallCount: 5 });
    }

    [Fact]
    public void MergeExternalLeaves_HostNamedLikeAService_AddsCallsToExistingEdge_NoDuplicateNode()
    {
        var nodes = new[] { Node("checkout"), Node("orders") };
        var edges = new[] { new ServiceDependencyEdge { Source = "checkout", Target = "orders", CallCount = 7, TotalDurationNano = 70 } };
        var leaves = new[] { new ServiceDependencyExternalLeaf("checkout", "orders", 3, 0, 30, ["GET"]) };

        var (mergedNodes, mergedEdges) = ServiceDependencyQueryBuilder.MergeExternalLeaves(nodes, edges, leaves);

        Assert.Equal(2, mergedNodes.Count);
        Assert.DoesNotContain(mergedNodes, n => n.IsExternal);
        var edge = Assert.Single(mergedEdges);
        Assert.Equal(10UL, edge.CallCount);
        Assert.Equal(100UL, edge.TotalDurationNano);
    }

    private static ServiceDependencyNode Node(string service) => new()
    {
        Service = service,
        SpanCount = 1,
        ErrorCount = 0,
        TotalDurationNano = 1,
        TopOperations = [],
    };
}
