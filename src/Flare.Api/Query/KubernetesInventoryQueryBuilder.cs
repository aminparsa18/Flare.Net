using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure SQL builder for the Kubernetes page's Nodes and Pods tables
/// (<c>POST /api/kubernetes/nodes</c>, <c>/api/kubernetes/nodes/metrics</c>,
/// <c>/api/kubernetes/pods</c>) - a cluster inventory derived entirely from the OTel
/// Collector's <c>kubeletstats</c> and <c>k8s_cluster</c> receivers' metrics already in
/// <c>metrics_gauge</c>/<c>metrics_sum</c>. No new table or ingest-side change, same
/// approach as <see cref="HostInventoryQueryBuilder"/>, whose window clamp and bucket width
/// this reuses (and <see cref="HostInventorySql"/> as its result type).
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity.</b> A node is a <c>k8s.node.name</c> resource attribute on any
/// <c>k8s.node.*</c> metric; a pod is a (<c>k8s.namespace.name</c>, <c>k8s.pod.name</c>)
/// pair on any <c>k8s.pod.*</c> metric. Both prefixes are <c>LIKE</c> prefix matches on the
/// tables' leading <c>ORDER BY</c> column, so they prune by primary key. A pod's node and
/// owning workload are the latest non-empty values seen, since only some senders set them
/// (<c>k8s.pod.phase</c> always carries <c>k8s.node.name</c>; kubeletstats' pod metrics only
/// do when the <c>k8sattributes</c> processor adds it).
/// </para>
/// <para>
/// <b>Which metric feeds which field</b> - all gauges, so no counter/reset handling:
/// <list type="bullet">
/// <item><b>Node CPU cores</b>: <c>k8s.node.cpu.usage</c> (or its deprecated name
/// <c>k8s.node.cpu.utilization</c>, same cores figure); <b>CPU %</b> divides it by the
/// latest <c>k8s.node.allocatable_cpu</c> (the <c>k8s_cluster</c> receiver's opt-in
/// <c>allocatable_types_to_report</c>) - computed in the service, since the two come from
/// different receivers with unrelated timestamps.</item>
/// <item><b>Node memory</b>: <c>k8s.node.memory.working_set</c>; <b>memory %</b> is working
/// set over working set + <c>k8s.node.memory.available</c> - the kubelet derives
/// <c>available</c> as capacity minus working set, so their sum is capacity. Computed per
/// scrape (both share its timestamp) and averaged, same as the Hosts page's ratios.</item>
/// <item><b>Node ready</b>: latest <c>k8s.node.condition_ready</c> (1/0/-1, reported by
/// <c>k8s_cluster</c>'s default <c>node_conditions_to_report: [Ready]</c>).</item>
/// <item><b>Node pods</b>: distinct pods seen on a <c>k8s.pod.*</c> gauge with that node name
/// in the window - pods that came and went during it count too.</item>
/// <item><b>Pod CPU/memory/limit %</b>: same metrics as <see cref="PodMetricsQueryBuilder"/>.</item>
/// <item><b>Pod phase</b>: latest <c>k8s.pod.phase</c>; <b>restarts</b>: each container's
/// latest <c>k8s.container.restarts</c>, summed.</item>
/// </list>
/// </para>
/// <para>
/// <b>Shape</b>: like the Hosts page, each list runs two statements - which objects exist
/// (capped, one row past the cap so the caller can tell it was cut), then their figures as
/// long-format <c>(Key, Kind, Figure)</c> rows for exactly those objects. The figure column
/// is deliberately not aliased <c>Value</c>: several branches aggregate the inner
/// <c>Value</c> column, and a same-named outer alias is the alias-shadowing trap
/// <see cref="HostInventoryQueryBuilder.BuildHostList"/> documents.
/// </para>
/// </remarks>
public static class KubernetesInventoryQueryBuilder
{
    public const int MaxNodes = 500;

    /// <summary>Higher than <see cref="MaxNodes"/> - a cluster runs many pods per node.</summary>
    public const int MaxPods = 1000;

    public const string CpuKind = "cpu";
    public const string MemoryKind = "memory";
    public const string MemoryPercentKind = "memory_pct";
    public const string AllocatableCpuKind = "alloc_cpu";
    public const string ReadyKind = "ready";
    public const string PodCountKind = "pods";
    public const string CpuLimitKind = "cpu_limit";
    public const string MemoryLimitKind = "memory_limit";
    public const string PhaseKind = "phase";
    public const string RestartsKind = "restarts";

    private const string NodeExpr = "ResourceAttributes['k8s.node.name']";
    private const string NamespaceExpr = "ResourceAttributes['k8s.namespace.name']";
    private const string PodExpr = "ResourceAttributes['k8s.pod.name']";

    /// <summary>A pod's list key - namespace and pod names are DNS labels/subdomains, so neither can contain <c>/</c>.</summary>
    private const string PodKeyExpr = $"concat({NamespaceExpr}, '/', {PodExpr})";

    private const string NodeCpuMetrics = "'k8s.node.cpu.usage', 'k8s.node.cpu.utilization'";
    private const string NodeAllocatableCpuMetrics = "'k8s.node.allocatable_cpu', 'k8s.node.allocatable.cpu'";
    private const string PodCpuMetrics = "'k8s.pod.cpu.usage', 'k8s.pod.cpu.utilization'";

    /// <summary>The service's key for a pod - must match <see cref="PodKeyExpr"/>.</summary>
    public static string PodKey(string podNamespace, string podName) => $"{podNamespace}/{podName}";

    /// <summary><c>k8s.pod.phase</c>'s encoding, per the <c>k8s_cluster</c> receiver's docs.</summary>
    public static string? DecodePhase(double value) => (int)Math.Round(value) switch
    {
        1 => "Pending",
        2 => "Running",
        3 => "Succeeded",
        4 => "Failed",
        5 => "Unknown",
        _ => null,
    };

    /// <summary><c>k8s.node.condition_ready</c>: 1 = true, 0 = false, -1 = unknown (null).</summary>
    public static bool? DecodeReady(double value) => value switch
    {
        >= 0.5 => true,
        >= -0.5 => false,
        _ => null,
    };

    /// <summary>CPU cores as a percentage of allocatable cores; null when either is missing or allocatable isn't positive.</summary>
    public static double? CpuPercent(double? cores, double? allocatableCores) =>
        cores is { } c && allocatableCores is > 0 ? 100 * c / allocatableCores.Value : null;

    /// <summary>Distinct nodes in the window with their cluster name and last-seen time, filtered and capped.</summary>
    public static HostInventorySql BuildNodeList(KubernetesNodeListRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("nodeLimit", (uint)(MaxNodes + 1));

        var extra = "";
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            extra += " AND positionCaseInsensitiveUTF8(Node, {search:String}) > 0";
        }

        if (!string.IsNullOrWhiteSpace(request.ClusterName))
        {
            parameters.AddParameter("clusterName", request.ClusterName.Trim());
            extra += " AND Cluster = {clusterName:String}";
        }

        string Branch(string table) =>
            $"  SELECT {NodeExpr} AS Node, ResourceAttributes['k8s.cluster.name'] AS Cluster, max(Time) AS LastSeen\n" +
            $"  FROM {table}\n" +
            $"  WHERE MetricName LIKE 'k8s.node.%' AND {time.WhereSql} AND Node != ''{extra}\n" +
            "  GROUP BY Node, Cluster\n";

        var sql = "SELECT Node, argMaxIf(Cluster, LastSeen, Cluster != '') AS LatestCluster, max(LastSeen) AS LatestSeen\n" +
            "FROM (\n" +
            Branch("metrics_gauge") +
            "  UNION ALL\n" +
            Branch("metrics_sum") +
            ")\n" +
            "GROUP BY Node\n" +
            "ORDER BY Node\n" +
            "LIMIT {nodeLimit:UInt32}";

        return new HostInventorySql(sql, parameters);
    }

    /// <summary>Whole-window figures for <paramref name="nodes"/>, as <c>(Key = node, Kind, Figure)</c> rows.</summary>
    public static HostInventorySql BuildNodeValues(IReadOnlyList<string> nodes, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("nodes", nodes.ToArray());
        var where = $"{time.WhereSql} AND {NodeExpr} IN {{nodes:Array(String)}}";

        var podCount =
            $"SELECT Node AS Key, '{PodCountKind}' AS Kind, CAST(uniqExact(Namespace, Pod) AS Nullable(Float64)) AS Figure\n" +
            "  FROM (\n" +
            $"    SELECT {NodeExpr} AS Node, {NamespaceExpr} AS Namespace, {PodExpr} AS Pod\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName LIKE 'k8s.pod.%' AND {where} AND Pod != ''\n" +
            "  )\n" +
            "  GROUP BY Key\n";

        return new HostInventorySql(
            BuildNodeValuesUnion("Node", where, includeReady: true) + "UNION ALL\n" + podCount,
            time.Parameters);
    }

    /// <summary>One node's CPU/memory figures per time bucket, as <c>(Key = BucketStart, Kind, Figure)</c> rows - including its allocatable CPU per bucket, for the service's CPU %.</summary>
    public static HostInventorySql BuildNodeMetrics(string nodeName, int windowMinutes, int bucketWidthSeconds, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("nodeName", nodeName);
        time.Parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);

        return new HostInventorySql(
            BuildNodeValuesUnion(
                "toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND)",
                $"{time.WhereSql} AND {NodeExpr} = {{nodeName:String}}",
                includeReady: false),
            time.Parameters);
    }

    /// <summary>Distinct pods in the window with their node, owning workload and last-seen time, filtered and capped.</summary>
    public static HostInventorySql BuildPodList(KubernetesPodListRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("podLimit", (uint)(MaxPods + 1));

        var extra = "";
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            extra += " AND positionCaseInsensitiveUTF8(Pod, {search:String}) > 0";
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            parameters.AddParameter("namespace", request.Namespace.Trim());
            extra += " AND Namespace = {namespace:String}";
        }

        // The node filter applies to the pod's *latest* node (HAVING), not per row - a row
        // without k8s.node.name (kubeletstats without k8sattributes) mustn't drop the pod.
        var having = "";
        if (!string.IsNullOrWhiteSpace(request.NodeName))
        {
            parameters.AddParameter("nodeName", request.NodeName.Trim());
            having = "HAVING LatestNode = {nodeName:String}\n";
        }

        // CronJob before Job: a CronJob's pods carry both names, and the CronJob is the
        // workload a user thinks in. ReplicaSet last: a Deployment's pods carry both.
        const string workloadKind =
            "multiIf(ResourceAttributes['k8s.deployment.name'] != '', 'Deployment',\n" +
            "      ResourceAttributes['k8s.statefulset.name'] != '', 'StatefulSet',\n" +
            "      ResourceAttributes['k8s.daemonset.name'] != '', 'DaemonSet',\n" +
            "      ResourceAttributes['k8s.cronjob.name'] != '', 'CronJob',\n" +
            "      ResourceAttributes['k8s.job.name'] != '', 'Job',\n" +
            "      ResourceAttributes['k8s.replicaset.name'] != '', 'ReplicaSet', '')";
        const string workloadName =
            "multiIf(ResourceAttributes['k8s.deployment.name'] != '', ResourceAttributes['k8s.deployment.name'],\n" +
            "      ResourceAttributes['k8s.statefulset.name'] != '', ResourceAttributes['k8s.statefulset.name'],\n" +
            "      ResourceAttributes['k8s.daemonset.name'] != '', ResourceAttributes['k8s.daemonset.name'],\n" +
            "      ResourceAttributes['k8s.cronjob.name'] != '', ResourceAttributes['k8s.cronjob.name'],\n" +
            "      ResourceAttributes['k8s.job.name'] != '', ResourceAttributes['k8s.job.name'],\n" +
            "      ResourceAttributes['k8s.replicaset.name'])";

        string Branch(string table) =>
            $"  SELECT {NamespaceExpr} AS Namespace, {PodExpr} AS Pod, {NodeExpr} AS Node,\n" +
            $"    {workloadKind} AS WorkloadKind,\n" +
            $"    {workloadName} AS WorkloadName,\n" +
            "    max(Time) AS LastSeen\n" +
            $"  FROM {table}\n" +
            $"  WHERE MetricName LIKE 'k8s.pod.%' AND {time.WhereSql} AND Pod != ''{extra}\n" +
            "  GROUP BY Namespace, Pod, Node, WorkloadKind, WorkloadName\n";

        var sql = "SELECT Namespace, Pod,\n" +
            "  argMaxIf(Node, LastSeen, Node != '') AS LatestNode,\n" +
            "  argMaxIf(WorkloadKind, LastSeen, WorkloadKind != '') AS LatestWorkloadKind,\n" +
            "  argMaxIf(WorkloadName, LastSeen, WorkloadName != '') AS LatestWorkloadName,\n" +
            "  max(LastSeen) AS LatestSeen\n" +
            "FROM (\n" +
            Branch("metrics_gauge") +
            "  UNION ALL\n" +
            Branch("metrics_sum") +
            ")\n" +
            "GROUP BY Namespace, Pod\n" +
            having +
            "ORDER BY Namespace, Pod\n" +
            "LIMIT {podLimit:UInt32}";

        return new HostInventorySql(sql, parameters);
    }

    /// <summary>Whole-window figures for the pods keyed by <paramref name="podKeys"/> (see <see cref="PodKey"/>), as <c>(Key, Kind, Figure)</c> rows.</summary>
    public static HostInventorySql BuildPodValues(IReadOnlyList<string> podKeys, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("pods", podKeys.ToArray());
        var where = $"{time.WhereSql} AND {PodKeyExpr} IN {{pods:Array(String)}}";

        // One pass over the pod gauges; phase is the latest reading, everything else an average.
        var usage =
            $"SELECT Key, Kind, CAST(if(Kind = '{PhaseKind}', argMax(Scaled, Time), avg(Scaled)) AS Nullable(Float64)) AS Figure\n" +
            "  FROM (\n" +
            $"    SELECT {PodKeyExpr} AS Key, Time,\n" +
            "      multiIf(\n" +
            $"        MetricName IN ({PodCpuMetrics}), '{CpuKind}',\n" +
            $"        MetricName = 'k8s.pod.memory.working_set', '{MemoryKind}',\n" +
            $"        MetricName = 'k8s.pod.cpu_limit_utilization', '{CpuLimitKind}',\n" +
            $"        MetricName = 'k8s.pod.memory_limit_utilization', '{MemoryLimitKind}',\n" +
            $"        '{PhaseKind}') AS Kind,\n" +
            "      if(endsWith(MetricName, '_limit_utilization'), 100 * Value, Value) AS Scaled\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName IN ({PodCpuMetrics}, 'k8s.pod.memory.working_set',\n" +
            "        'k8s.pod.cpu_limit_utilization', 'k8s.pod.memory_limit_utilization', 'k8s.pod.phase')\n" +
            $"      AND {where}\n" +
            "  )\n" +
            "  GROUP BY Key, Kind\n";

        var restarts =
            $"SELECT Key, '{RestartsKind}' AS Kind, CAST(sum(Latest) AS Nullable(Float64)) AS Figure\n" +
            "  FROM (\n" +
            $"    SELECT {PodKeyExpr} AS Key, ResourceAttributes['k8s.container.name'] AS Container, argMax(Value, Time) AS Latest\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName = 'k8s.container.restarts' AND {where}\n" +
            "    GROUP BY Key, Container\n" +
            "  )\n" +
            "  GROUP BY Key\n";

        return new HostInventorySql(usage + "UNION ALL\n" + restarts, time.Parameters);
    }

    private static MetricFilterSql TimeFilter(int windowMinutes, DateTimeOffset now) =>
        MetricFilterSqlBuilder.Build(new MetricFilter { From = now - TimeSpan.FromMinutes(windowMinutes), To = now }, now);

    /// <summary>
    /// The node CPU/memory/allocatable(/ready) pass plus the memory-% pass, grouped by
    /// <paramref name="keyExpr"/> (the node for the list, a time bucket for the drill-down).
    /// Allocatable and ready are latest readings; CPU and memory are averages.
    /// </summary>
    private static string BuildNodeValuesUnion(string keyExpr, string where, bool includeReady)
    {
        var metrics = $"{NodeCpuMetrics}, 'k8s.node.memory.working_set', {NodeAllocatableCpuMetrics}" +
            (includeReady ? ", 'k8s.node.condition_ready'" : "");

        var gauges =
            $"SELECT {keyExpr} AS Key, Kind,\n" +
            $"    CAST(if(Kind IN ('{AllocatableCpuKind}', '{ReadyKind}'), argMax(Value, Time), avg(Value)) AS Nullable(Float64)) AS Figure\n" +
            "  FROM (\n" +
            $"    SELECT {NodeExpr} AS Node, Time, Value,\n" +
            "      multiIf(\n" +
            $"        MetricName IN ({NodeCpuMetrics}), '{CpuKind}',\n" +
            $"        MetricName = 'k8s.node.memory.working_set', '{MemoryKind}',\n" +
            $"        MetricName IN ({NodeAllocatableCpuMetrics}), '{AllocatableCpuKind}',\n" +
            $"        '{ReadyKind}') AS Kind\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName IN ({metrics}) AND {where}\n" +
            "  )\n" +
            "  GROUP BY Key, Kind\n";

        // A scrape missing either half is skipped rather than read as 0% or 100%.
        var memoryPercent =
            $"SELECT {keyExpr} AS Key, '{MemoryPercentKind}' AS Kind, CAST(100 * avg(Ratio) AS Nullable(Float64)) AS Figure\n" +
            "  FROM (\n" +
            $"    SELECT {NodeExpr} AS Node, Time,\n" +
            "      sumIf(Value, MetricName = 'k8s.node.memory.working_set') / nullIf(sum(Value), 0) AS Ratio\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName IN ('k8s.node.memory.working_set', 'k8s.node.memory.available') AND {where}\n" +
            "    GROUP BY Node, Time\n" +
            "    HAVING countIf(MetricName = 'k8s.node.memory.working_set') > 0 AND countIf(MetricName = 'k8s.node.memory.available') > 0\n" +
            "  )\n" +
            "  GROUP BY Key\n";

        return gauges + "UNION ALL\n" + memoryPercent;
    }
}
