using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class KubernetesWorkloadQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static KubernetesWorkloadKind Kind(string name) => KubernetesWorkloadQueryBuilder.ResolveKind(name)!;

    private static KubernetesWorkloadListRequest ListRequest(string kind) => new() { Kind = kind };

    [Theory]
    [InlineData("Deployment", "Deployment")]
    [InlineData("deployment", "Deployment")]
    [InlineData(" statefulset ", "StatefulSet")]
    [InlineData("DAEMONSET", "DaemonSet")]
    [InlineData("job", "Job")]
    [InlineData("CronJob", "CronJob")]
    public void ResolveKind_IsCaseInsensitive_AndReturnsCanonicalCasing(string requested, string expected)
    {
        Assert.Equal(expected, KubernetesWorkloadQueryBuilder.ResolveKind(requested)?.Kind);
    }

    [Theory]
    [InlineData("ReplicaSet")]
    [InlineData("Pod")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveKind_UnknownKind_IsNull(string? requested)
    {
        Assert.Null(KubernetesWorkloadQueryBuilder.ResolveKind(requested));
    }

    [Fact]
    public void Kinds_EveryCountHasAtLeastOneMetric_AndMatchesItsKindsPrefix()
    {
        foreach (var kind in KubernetesWorkloadQueryBuilder.Kinds)
        {
            var prefix = kind.MetricPrefix.TrimEnd('%');
            Assert.NotEmpty(kind.Counts);
            Assert.All(kind.Counts, count =>
            {
                Assert.NotEmpty(count.MetricNames);
                Assert.All(count.MetricNames, name => Assert.StartsWith(prefix, name));
            });
        }
    }

    [Fact]
    public void BuildWorkloadList_UnionsGaugeAndSum_OnTheKindsMetricsOrItsPods()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildWorkloadList(Kind("Deployment"), ListRequest("Deployment"), 60, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "(MetricName LIKE 'k8s.deployment.%' OR MetricName LIKE 'k8s.pod.%')"));
        Assert.Contains("ResourceAttributes['k8s.deployment.name'] AS Name", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "Name != ''"));
        Assert.Equal((uint)(KubernetesWorkloadQueryBuilder.MaxWorkloads + 1), result.Parameters.ToDictionary()["workloadLimit"]);
    }

    [Fact]
    public void BuildWorkloadList_Filters_AreTrimmedAndBoundInBothBranches()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildWorkloadList(
            Kind("StatefulSet"), new KubernetesWorkloadListRequest { Kind = "StatefulSet", Search = " db ", Namespace = " data " }, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal("db", parameters["search"]);
        Assert.Equal("data", parameters["namespace"]);
        Assert.Equal(2, CountOccurrences(result.Sql, "positionCaseInsensitiveUTF8(Name, {search:String}) > 0"));
        Assert.Equal(2, CountOccurrences(result.Sql, "Namespace = {namespace:String}"));
        Assert.Contains("ResourceAttributes['k8s.statefulset.name'] AS Name", result.Sql);
    }

    [Fact]
    public void BuildWorkloadValues_ReadsTheKindsCountsAsLatest_PlusPodUsageAndCount()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildWorkloadValues(
            Kind("Job"), [KubernetesWorkloadQueryBuilder.WorkloadKey("batch", "nightly")], 60, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(new[] { "batch/nightly" }, parameters["workloads"]);
        Assert.Equal(60u, parameters["bucketWidth"]);
        Assert.Contains("concat(ResourceAttributes['k8s.namespace.name'], '/', ResourceAttributes['k8s.job.name']) IN {workloads:Array(String)}", result.Sql);
        Assert.Contains("argMax(Value, Time)", result.Sql);
        Assert.Contains("MetricName IN ('k8s.job.desired_successful_pods', 'k8s.job.pod.desired_successful'), 'desired'", result.Sql);
        Assert.Contains("'k8s.job.failed_pods', 'k8s.job.pod.failed'", result.Sql);
        // The last count is the multiIf's fallthrough - the WHERE only admits the counts' metrics.
        Assert.Contains("        'failed')", result.Sql);
        Assert.Contains("uniqExact(Pod)", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "UNION ALL"));
    }

    [Fact]
    public void BuildWorkloadValues_SingleCountKind_UsesALiteralKind()
    {
        var sql = KubernetesWorkloadQueryBuilder.BuildWorkloadValues(Kind("CronJob"), ["ns/c"], 60, 60, Now).Sql;

        Assert.Contains("'active' AS Kind", sql);
        Assert.Contains("MetricName IN ('k8s.cronjob.active_jobs', 'k8s.cronjob.job.active')", sql);
    }

    [Fact]
    public void BuildWorkloadValues_PodUsage_SumsPerBucketThenAverages()
    {
        var sql = KubernetesWorkloadQueryBuilder.BuildWorkloadValues(Kind("Deployment"), ["ns/web"], 60, 60, Now).Sql;

        Assert.Contains("avg(Value) AS PodAverage", sql);
        Assert.Contains("GROUP BY Key, Bucket, Pod, Kind", sql);
        Assert.Contains("sum(PodAverage) AS Total", sql);
        Assert.Contains("GROUP BY Key, Bucket, Kind", sql);
        Assert.Contains("avg(Total)", sql);
        Assert.Contains("'k8s.pod.cpu.usage', 'k8s.pod.cpu.utilization', 'k8s.pod.memory.working_set'", sql);
    }

    [Fact]
    public void BuildWorkloadMetrics_KeysByBucket_ForOneWorkload_WithoutPodCount()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildWorkloadMetrics(Kind("DaemonSet"), "kube-system", "fluent-bit", 60, 120, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal("kube-system", parameters["namespace"]);
        Assert.Equal("fluent-bit", parameters["name"]);
        Assert.Equal(120u, parameters["bucketWidth"]);
        Assert.Contains("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS Key", result.Sql);
        Assert.Contains("ResourceAttributes['k8s.daemonset.name'] = {name:String}", result.Sql);
        Assert.Contains("'k8s.daemonset.ready_nodes'", result.Sql);
        Assert.Contains("'k8s.daemonset.misscheduled_nodes'", result.Sql);
        Assert.Contains("'k8s.daemonset.node.misscheduled'", result.Sql);
        Assert.Contains("'misscheduled'", result.Sql);
        Assert.DoesNotContain("uniqExact", result.Sql);
    }

    [Fact]
    public void BuildNamespaceList_MatchesAnyK8sMetricWithANamespace()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildNamespaceList(new KubernetesNamespaceListRequest { Search = " shop " }, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(2, CountOccurrences(result.Sql, "MetricName LIKE 'k8s.%'"));
        Assert.Equal(2, CountOccurrences(result.Sql, "Namespace != ''"));
        Assert.Equal("shop", parameters["search"]);
        Assert.Equal((uint)(KubernetesWorkloadQueryBuilder.MaxNamespaces + 1), parameters["namespaceLimit"]);
    }

    [Fact]
    public void BuildNamespaceValues_ReadsPhasePodsAndUsage()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildNamespaceValues(["shop"], 60, 60, Now);

        Assert.Equal(new[] { "shop" }, result.Parameters.ToDictionary()["namespaces"]);
        Assert.Contains("MetricName IN ('k8s.namespace.phase')", result.Sql);
        Assert.Contains("'phase' AS Kind", result.Sql);
        Assert.Contains("sum(PodAverage)", result.Sql);
        Assert.Contains("uniqExact(Pod)", result.Sql);
    }

    [Fact]
    public void BuildVolumeList_ResolvesLatestTypeAndClaim_SearchMatchesVolumeOrClaim()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildVolumeList(
            new KubernetesVolumeListRequest { Search = "data", Namespace = "db" }, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(2, CountOccurrences(result.Sql, "MetricName LIKE 'k8s.volume.%'"));
        Assert.Contains("argMaxIf(Claim, LastSeen, Claim != '') AS LatestClaim", result.Sql);
        Assert.Contains("GROUP BY Namespace, Pod, Volume\n", result.Sql);
        Assert.Contains("HAVING positionCaseInsensitiveUTF8(Volume, {search:String}) > 0 OR positionCaseInsensitiveUTF8(LatestClaim, {search:String}) > 0", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "Namespace = {namespace:String}"));
        Assert.Equal("data", parameters["search"]);
        Assert.Equal((uint)(KubernetesWorkloadQueryBuilder.MaxVolumes + 1), parameters["volumeLimit"]);
    }

    [Fact]
    public void BuildVolumeValues_KeysByNamespacePodVolume_LatestReadings()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildVolumeValues([KubernetesWorkloadQueryBuilder.VolumeKey("db", "pg-0", "data")], 60, Now);

        Assert.Equal(new[] { "db/pg-0/data" }, result.Parameters.ToDictionary()["volumes"]);
        Assert.Contains("concat(ResourceAttributes['k8s.namespace.name'], '/', ResourceAttributes['k8s.pod.name'], '/', ResourceAttributes['k8s.volume.name'])", result.Sql);
        Assert.Contains("argMax(Value, Time)", result.Sql);
        Assert.Contains("'k8s.volume.capacity'", result.Sql);
        Assert.Contains("'k8s.volume.inodes.free'", result.Sql);
    }

    [Fact]
    public void BuildVolumeMetrics_KeysByBucket_ForOneVolume()
    {
        var result = KubernetesWorkloadQueryBuilder.BuildVolumeMetrics("db", "pg-0", "data", 60, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal("pg-0", parameters["podName"]);
        Assert.Equal("data", parameters["volumeName"]);
        Assert.Contains("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS Key", result.Sql);
        Assert.Contains("ResourceAttributes['k8s.volume.name'] = {volumeName:String}", result.Sql);
    }

    [Fact]
    public void FigureColumn_IsNeverAliasedValue()
    {
        Assert.DoesNotContain("AS Value", KubernetesWorkloadQueryBuilder.BuildWorkloadValues(Kind("Deployment"), ["a/b"], 60, 60, Now).Sql);
        Assert.DoesNotContain("AS Value", KubernetesWorkloadQueryBuilder.BuildWorkloadMetrics(Kind("Job"), "a", "b", 60, 60, Now).Sql);
        Assert.DoesNotContain("AS Value", KubernetesWorkloadQueryBuilder.BuildNamespaceValues(["a"], 60, 60, Now).Sql);
        Assert.DoesNotContain("AS Value", KubernetesWorkloadQueryBuilder.BuildVolumeValues(["a/b/c"], 60, Now).Sql);
    }

    [Theory]
    [InlineData(1, "Active")]
    [InlineData(0, "Terminating")]
    [InlineData(2, null)]
    public void DecodeNamespacePhase_MapsActiveTerminating(double value, string? expected)
    {
        Assert.Equal(expected, KubernetesWorkloadQueryBuilder.DecodeNamespacePhase(value));
    }

    [Theory]
    [InlineData(100.0, 25.0, 75.0, 75.0)]
    [InlineData(100.0, 120.0, 0.0, 0.0)]
    [InlineData(null, 25.0, null, null)]
    [InlineData(100.0, null, null, null)]
    [InlineData(0.0, 0.0, 0.0, null)]
    public void UsedBytesAndPercent_NeedCapacityAndAvailable(double? capacity, double? available, double? usedBytes, double? usedPercent)
    {
        Assert.Equal(usedBytes, KubernetesWorkloadQueryBuilder.UsedBytes(capacity, available));
        Assert.Equal(usedPercent, KubernetesWorkloadQueryBuilder.UsedPercent(capacity, available));
    }

    [Theory]
    [InlineData(1000.0, 250.0, null, 25.0)]
    [InlineData(1000.0, null, 900.0, 10.0)]
    [InlineData(1000.0, 250.0, 900.0, 25.0)]
    [InlineData(1000.0, null, null, null)]
    [InlineData(null, 250.0, null, null)]
    public void InodesUsedPercent_PrefersUsed_FallsBackToTotalMinusFree(double? total, double? used, double? free, double? expected)
    {
        Assert.Equal(expected, KubernetesWorkloadQueryBuilder.InodesUsedPercent(total, used, free));
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
