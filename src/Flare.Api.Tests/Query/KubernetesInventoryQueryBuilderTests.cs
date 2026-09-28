using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class KubernetesInventoryQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildNodeList_UnionsGaugeAndSum_OnNodeMetricsWithANodeName()
    {
        var result = KubernetesInventoryQueryBuilder.BuildNodeList(new KubernetesNodeListRequest(), 60, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "MetricName LIKE 'k8s.node.%'"));
        Assert.Equal(2, CountOccurrences(result.Sql, "Node != ''"));
        Assert.Contains("ResourceAttributes['k8s.node.name'] AS Node", result.Sql);
        Assert.Contains("argMaxIf(Cluster, LastSeen, Cluster != '') AS LatestCluster", result.Sql);
    }

    [Fact]
    public void BuildNodeList_BindsWindowAndFetchesOneRowPastTheCap()
    {
        var parameters = KubernetesInventoryQueryBuilder.BuildNodeList(new KubernetesNodeListRequest(), 60, Now).Parameters.ToDictionary();

        Assert.Equal(Now.AddMinutes(-60).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
        Assert.Equal((uint)(KubernetesInventoryQueryBuilder.MaxNodes + 1), parameters["nodeLimit"]);
    }

    [Fact]
    public void BuildNodeList_Filters_AreTrimmedAndBoundInBothBranches()
    {
        var result = KubernetesInventoryQueryBuilder.BuildNodeList(
            new KubernetesNodeListRequest { Search = " worker ", ClusterName = " prod " }, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal("worker", parameters["search"]);
        Assert.Equal("prod", parameters["clusterName"]);
        Assert.Equal(2, CountOccurrences(result.Sql, "positionCaseInsensitiveUTF8(Node, {search:String}) > 0"));
        Assert.Equal(2, CountOccurrences(result.Sql, "Cluster = {clusterName:String}"));
    }

    [Fact]
    public void BuildNodeList_BlankFilters_BindNothing()
    {
        var parameters = KubernetesInventoryQueryBuilder.BuildNodeList(
            new KubernetesNodeListRequest { Search = "  ", ClusterName = "" }, 60, Now).Parameters.ToDictionary();

        Assert.False(parameters.ContainsKey("search"));
        Assert.False(parameters.ContainsKey("clusterName"));
    }

    [Fact]
    public void BuildNodeValues_ReadsEveryKindForTheGivenNodes()
    {
        var result = KubernetesInventoryQueryBuilder.BuildNodeValues(["a", "b"], 60, Now);

        Assert.Equal(new[] { "a", "b" }, result.Parameters.ToDictionary()["nodes"]);
        Assert.Contains("'k8s.node.cpu.usage', 'k8s.node.cpu.utilization'", result.Sql);
        Assert.Contains("'k8s.node.memory.working_set'", result.Sql);
        Assert.Contains("'k8s.node.memory.available'", result.Sql);
        Assert.Contains("'k8s.node.allocatable_cpu', 'k8s.node.allocatable.cpu'", result.Sql);
        Assert.Contains("'k8s.node.condition_ready'", result.Sql);
        Assert.Contains("uniqExact(Namespace, Pod)", result.Sql);
        Assert.Contains("MetricName LIKE 'k8s.pod.%'", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "UNION ALL"));
    }

    [Fact]
    public void BuildNodeValues_LatestReadingForAllocatableAndReady_AverageForUsage()
    {
        var sql = KubernetesInventoryQueryBuilder.BuildNodeValues(["a"], 60, Now).Sql;

        Assert.Contains("if(Kind IN ('alloc_cpu', 'ready'), argMax(Value, Time), avg(Value))", sql);
    }

    [Fact]
    public void BuildNodeValues_MemoryPercent_SkipsScrapesMissingEitherHalf()
    {
        var sql = KubernetesInventoryQueryBuilder.BuildNodeValues(["a"], 60, Now).Sql;

        Assert.Contains("GROUP BY Node, Time", sql);
        Assert.Contains("HAVING countIf(MetricName = 'k8s.node.memory.working_set') > 0 AND countIf(MetricName = 'k8s.node.memory.available') > 0", sql);
    }

    [Fact]
    public void FigureColumn_IsNeverAliasedValue()
    {
        // An outer `AS Value` next to aggregates of the inner Value column is the
        // alias-shadowing trap HostInventoryQueryBuilder.BuildHostList documents.
        Assert.DoesNotContain("AS Value", KubernetesInventoryQueryBuilder.BuildNodeValues(["a"], 60, Now).Sql);
        Assert.DoesNotContain("AS Value", KubernetesInventoryQueryBuilder.BuildNodeMetrics("a", 60, 60, Now).Sql);
        Assert.DoesNotContain("AS Value", KubernetesInventoryQueryBuilder.BuildPodValues(["ns/p"], 60, Now).Sql);
    }

    [Fact]
    public void BuildNodeMetrics_KeysByBucket_ForOneNode_WithoutReadyOrPodCount()
    {
        var result = KubernetesInventoryQueryBuilder.BuildNodeMetrics("worker-1", 60, 120, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal("worker-1", parameters["nodeName"]);
        Assert.Equal(120u, parameters["bucketWidth"]);
        Assert.Contains("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS Key", result.Sql);
        Assert.Contains("ResourceAttributes['k8s.node.name'] = {nodeName:String}", result.Sql);
        Assert.DoesNotContain("k8s.node.condition_ready", result.Sql);
        Assert.DoesNotContain("uniqExact", result.Sql);
    }

    [Fact]
    public void BuildPodList_UnionsGaugeAndSum_OnPodMetrics_ResolvingLatestNonEmptyMetadata()
    {
        var result = KubernetesInventoryQueryBuilder.BuildPodList(new KubernetesPodListRequest(), 60, Now);

        Assert.Equal(2, CountOccurrences(result.Sql, "MetricName LIKE 'k8s.pod.%'"));
        Assert.Contains("argMaxIf(Node, LastSeen, Node != '') AS LatestNode", result.Sql);
        Assert.Contains("argMaxIf(WorkloadKind, LastSeen, WorkloadKind != '') AS LatestWorkloadKind", result.Sql);
        Assert.Contains("GROUP BY Namespace, Pod\n", result.Sql);
        Assert.DoesNotContain("HAVING", result.Sql);
        Assert.Equal((uint)(KubernetesInventoryQueryBuilder.MaxPods + 1), result.Parameters.ToDictionary()["podLimit"]);
    }

    [Fact]
    public void BuildPodList_WorkloadKind_PrefersCronJobOverJob_AndDeploymentOverReplicaSet()
    {
        var sql = KubernetesInventoryQueryBuilder.BuildPodList(new KubernetesPodListRequest(), 60, Now).Sql;

        Assert.True(sql.IndexOf("'CronJob'", StringComparison.Ordinal) < sql.IndexOf("'Job'", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("'Deployment'", StringComparison.Ordinal) < sql.IndexOf("'ReplicaSet'", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPodList_NamespaceAndSearchFilterRows_NodeFiltersTheLatestNode()
    {
        var result = KubernetesInventoryQueryBuilder.BuildPodList(
            new KubernetesPodListRequest { Search = "web", Namespace = "shop", NodeName = "worker-1" }, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal("web", parameters["search"]);
        Assert.Equal("shop", parameters["namespace"]);
        Assert.Equal("worker-1", parameters["nodeName"]);
        Assert.Equal(2, CountOccurrences(result.Sql, "Namespace = {namespace:String}"));
        Assert.Contains("HAVING LatestNode = {nodeName:String}", result.Sql);
        Assert.DoesNotContain("Node = {nodeName", result.Sql.Replace("LatestNode = {nodeName", ""));
    }

    [Fact]
    public void BuildPodValues_KeysByNamespaceSlashPod_AndSumsLatestRestartsPerContainer()
    {
        var result = KubernetesInventoryQueryBuilder.BuildPodValues([KubernetesInventoryQueryBuilder.PodKey("shop", "web-0")], 60, Now);

        Assert.Equal(new[] { "shop/web-0" }, result.Parameters.ToDictionary()["pods"]);
        Assert.Contains("concat(ResourceAttributes['k8s.namespace.name'], '/', ResourceAttributes['k8s.pod.name']) IN {pods:Array(String)}", result.Sql);
        Assert.Contains("if(Kind = 'phase', argMax(Scaled, Time), avg(Scaled))", result.Sql);
        Assert.Contains("'k8s.pod.phase'", result.Sql);
        Assert.Contains("MetricName = 'k8s.container.restarts'", result.Sql);
        Assert.Contains("argMax(Value, Time) AS Latest", result.Sql);
        Assert.Contains("GROUP BY Key, Container", result.Sql);
        Assert.Contains("sum(Latest)", result.Sql);
    }

    [Theory]
    [InlineData(1, "Pending")]
    [InlineData(2, "Running")]
    [InlineData(3, "Succeeded")]
    [InlineData(4, "Failed")]
    [InlineData(5, "Unknown")]
    [InlineData(0, null)]
    [InlineData(9, null)]
    public void DecodePhase_MapsTheReceiversEncoding(double value, string? expected)
    {
        Assert.Equal(expected, KubernetesInventoryQueryBuilder.DecodePhase(value));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, null)]
    public void DecodeReady_MapsTrueFalseUnknown(double value, bool? expected)
    {
        Assert.Equal(expected, KubernetesInventoryQueryBuilder.DecodeReady(value));
    }

    [Theory]
    [InlineData(1.0, 4.0, 25.0)]
    [InlineData(1.0, null, null)]
    [InlineData(1.0, 0.0, null)]
    [InlineData(null, 4.0, null)]
    public void CpuPercent_NeedsBothFiguresAndAPositiveAllocatable(double? cores, double? allocatable, double? expected)
    {
        Assert.Equal(expected, KubernetesInventoryQueryBuilder.CpuPercent(cores, allocatable));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
