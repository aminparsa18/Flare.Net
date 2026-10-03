using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A workload count's figure kind and the metric names (current name first, then semconv/legacy aliases) that feed it.</summary>
public sealed record KubernetesWorkloadCount(string FigureKind, IReadOnlyList<string> MetricNames);

/// <summary>
/// One workload kind the Kubernetes page lists: the resource attribute naming a workload of
/// that kind, the <c>k8s_cluster</c> receiver's metric prefix for it, and which of its
/// metrics feed which <see cref="KubernetesWorkloadSummary"/> count.
/// </summary>
public sealed record KubernetesWorkloadKind(string Kind, string NameAttribute, string MetricPrefix, IReadOnlyList<KubernetesWorkloadCount> Counts);

/// <summary>
/// Pure SQL builder for the Kubernetes page's Workloads, Namespaces and Volumes tables and the
/// workload/volume drill-downs (<c>POST /api/kubernetes/workloads</c>,
/// <c>/workloads/metrics</c>, <c>/namespaces</c>, <c>/volumes</c>, <c>/volumes/metrics</c>).
/// Same source metrics, window clamp, two-statement list shape and <c>(Key, Kind, Figure)</c>
/// long format as <see cref="KubernetesInventoryQueryBuilder"/> - see its remarks - and, like
/// it, no migration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity.</b> A workload of kind K is a (<c>k8s.namespace.name</c>, K's name
/// attribute) pair on either one of the <c>k8s_cluster</c> receiver's <c>k8s.&lt;kind&gt;.*</c>
/// metrics or a <c>k8s.pod.*</c> metric carrying that attribute (which the
/// <c>k8sattributes</c> processor adds) - so a workload shows up with only one of the two
/// receivers running, just with that half's figures. A namespace is any
/// <c>k8s.namespace.name</c> on any <c>k8s.*</c> metric; a volume is a (namespace, pod,
/// <c>k8s.volume.name</c>) triple on <c>kubeletstats</c>' <c>k8s.volume.*</c> metrics.
/// </para>
/// <para>
/// <b>Counts</b> (desired/ready/...) are each metric's latest reading - see
/// <see cref="Kinds"/> for which metric feeds which field per kind. Every kind also accepts
/// the newer semantic-conventions names (<c>k8s.deployment.pod.desired</c>, ...) alongside
/// the receiver's current ones.
/// </para>
/// <para>
/// <b>Pod usage</b> (CPU/memory for a workload or namespace) is each pod's average per time
/// bucket, summed over the pods per bucket, then averaged over the buckets - so it reads as
/// "total usage at a typical moment", and a rolling update's old and new pods only overlap
/// in the buckets where both actually ran, instead of the whole window's per-pod averages
/// being added up. The bucket is the drill-down chart's width
/// (<see cref="HostInventoryQueryBuilder.BucketWidthSecondsFor"/>), so a list figure is the
/// average of its chart's points.
/// </para>
/// </remarks>
public static class KubernetesWorkloadQueryBuilder
{
    public const int MaxWorkloads = 1000;
    public const int MaxNamespaces = 500;
    public const int MaxVolumes = 1000;

    public const string DesiredKind = "desired";
    public const string ReadyKind = "ready";
    public const string CurrentKind = "current";
    public const string MisscheduledKind = "misscheduled";
    public const string ActiveKind = "active";
    public const string SucceededKind = "succeeded";
    public const string FailedKind = "failed";
    public const string PodCountKind = "pods";
    public const string CpuKind = "cpu";
    public const string MemoryKind = "memory";
    public const string NamespacePhaseKind = "phase";
    public const string CapacityKind = "capacity";
    public const string AvailableKind = "available";
    public const string InodesKind = "inodes";
    public const string InodesUsedKind = "inodes_used";
    public const string InodesFreeKind = "inodes_free";

    private const string NamespaceExpr = "ResourceAttributes['k8s.namespace.name']";
    private const string PodExpr = "ResourceAttributes['k8s.pod.name']";
    private const string VolumeExpr = "ResourceAttributes['k8s.volume.name']";
    private const string VolumeKeyExpr = $"concat({NamespaceExpr}, '/', {PodExpr}, '/', {VolumeExpr})";
    private const string PodCpuMetrics = "'k8s.pod.cpu.usage', 'k8s.pod.cpu.utilization'";

    private static KubernetesWorkloadCount Count(string kind, params string[] metricNames) => new(kind, metricNames);

    /// <summary>
    /// Every listable workload kind. ReplicaSets are left out on purpose: a Deployment's
    /// ReplicaSets are an implementation detail of its rollouts, and its pods already resolve
    /// to the Deployment (see <see cref="KubernetesInventoryQueryBuilder.BuildPodList"/>).
    /// </summary>
    public static readonly IReadOnlyList<KubernetesWorkloadKind> Kinds =
    [
        new("Deployment", "k8s.deployment.name", "k8s.deployment.%",
        [
            Count(DesiredKind, "k8s.deployment.desired", "k8s.deployment.pod.desired"),
            Count(ReadyKind, "k8s.deployment.available", "k8s.deployment.pod.available"),
        ]),
        new("StatefulSet", "k8s.statefulset.name", "k8s.statefulset.%",
        [
            Count(DesiredKind, "k8s.statefulset.desired_pods", "k8s.statefulset.pod.desired"),
            Count(ReadyKind, "k8s.statefulset.ready_pods", "k8s.statefulset.pod.ready"),
            Count(CurrentKind, "k8s.statefulset.current_pods", "k8s.statefulset.pod.current"),
        ]),
        new("DaemonSet", "k8s.daemonset.name", "k8s.daemonset.%",
        [
            Count(DesiredKind, "k8s.daemonset.desired_scheduled_nodes", "k8s.daemonset.node.desired_scheduled"),
            Count(ReadyKind, "k8s.daemonset.ready_nodes", "k8s.daemonset.node.ready"),
            Count(CurrentKind, "k8s.daemonset.current_scheduled_nodes", "k8s.daemonset.node.current_scheduled"),
            Count(MisscheduledKind, "k8s.daemonset.misscheduled_nodes", "k8s.daemonset.node.misscheduled"),
        ]),
        new("Job", "k8s.job.name", "k8s.job.%",
        [
            Count(DesiredKind, "k8s.job.desired_successful_pods", "k8s.job.pod.desired_successful"),
            Count(ActiveKind, "k8s.job.active_pods", "k8s.job.pod.active"),
            Count(SucceededKind, "k8s.job.successful_pods", "k8s.job.pod.successful"),
            Count(FailedKind, "k8s.job.failed_pods", "k8s.job.pod.failed"),
        ]),
        new("CronJob", "k8s.cronjob.name", "k8s.cronjob.%",
        [
            Count(ActiveKind, "k8s.cronjob.active_jobs", "k8s.cronjob.job.active"),
        ]),
    ];

    private static readonly IReadOnlyList<KubernetesWorkloadCount> NamespaceCounts = [Count(NamespacePhaseKind, "k8s.namespace.phase")];

    private static readonly IReadOnlyList<KubernetesWorkloadCount> VolumeCounts =
    [
        Count(CapacityKind, "k8s.volume.capacity"),
        Count(AvailableKind, "k8s.volume.available"),
        Count(InodesKind, "k8s.volume.inodes"),
        Count(InodesUsedKind, "k8s.volume.inodes.used"),
        Count(InodesFreeKind, "k8s.volume.inodes.free"),
    ];

    /// <summary>The kind named <paramref name="kind"/> (case-insensitive), or null when it isn't one of <see cref="Kinds"/>.</summary>
    public static KubernetesWorkloadKind? ResolveKind(string? kind) =>
        Kinds.FirstOrDefault(k => string.Equals(k.Kind, kind?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A workload's list key - namespace and workload names are DNS subdomains, so neither contains <c>/</c>.</summary>
    public static string WorkloadKey(string workloadNamespace, string name) => $"{workloadNamespace}/{name}";

    public static string VolumeKey(string volumeNamespace, string podName, string volumeName) => $"{volumeNamespace}/{podName}/{volumeName}";

    /// <summary><c>k8s.namespace.phase</c>: 1 = active, 0 = terminating.</summary>
    public static string? DecodeNamespacePhase(double value) => (int)Math.Round(value) switch
    {
        1 => "Active",
        0 => "Terminating",
        _ => null,
    };

    /// <summary>Used bytes from capacity and available; null unless both are known.</summary>
    public static double? UsedBytes(double? capacity, double? available) =>
        capacity is { } c && available is { } a ? Math.Max(0, c - a) : null;

    /// <summary>Used as a percentage of capacity; null unless both are known and capacity is positive.</summary>
    public static double? UsedPercent(double? capacity, double? available) =>
        capacity is > 0 && available is { } a ? 100 * Math.Max(0, capacity.Value - a) / capacity.Value : null;

    /// <summary>Used inodes as a percentage of total - from <c>inodes.used</c>, or total minus <c>inodes.free</c> when only that is reported.</summary>
    public static double? InodesUsedPercent(double? total, double? used, double? free) =>
        total is > 0 && (used ?? (free is { } f ? total.Value - f : null)) is { } u ? 100 * Math.Max(0, u) / total.Value : null;

    private static string WorkloadKeyExpr(KubernetesWorkloadKind kind) => $"concat({NamespaceExpr}, '/', {NameExpr(kind)})";

    private static string NameExpr(KubernetesWorkloadKind kind) => $"ResourceAttributes['{kind.NameAttribute}']";

    /// <summary>Distinct workloads of <paramref name="kind"/> in the window with their last-seen time, filtered and capped.</summary>
    public static HostInventorySql BuildWorkloadList(KubernetesWorkloadKind kind, KubernetesWorkloadListRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("workloadLimit", (uint)(MaxWorkloads + 1));

        var extra = "";
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            extra += " AND positionCaseInsensitiveUTF8(Name, {search:String}) > 0";
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            parameters.AddParameter("namespace", request.Namespace.Trim());
            extra += " AND Namespace = {namespace:String}";
        }

        string Branch(string table) =>
            $"  SELECT {NamespaceExpr} AS Namespace, {NameExpr(kind)} AS Name, max(Time) AS LastSeen\n" +
            $"  FROM {table}\n" +
            $"  WHERE (MetricName LIKE '{kind.MetricPrefix}' OR MetricName LIKE 'k8s.pod.%') AND {time.WhereSql} AND Name != ''{extra}\n" +
            "  GROUP BY Namespace, Name\n";

        var sql = "SELECT Namespace, Name, max(LastSeen) AS LatestSeen\n" +
            "FROM (\n" +
            Branch("metrics_gauge") +
            "  UNION ALL\n" +
            Branch("metrics_sum") +
            ")\n" +
            "GROUP BY Namespace, Name\n" +
            "ORDER BY Namespace, Name\n" +
            "LIMIT {workloadLimit:UInt32}";

        return new HostInventorySql(sql, parameters);
    }

    /// <summary>Whole-window figures for the workloads keyed by <paramref name="workloadKeys"/> (see <see cref="WorkloadKey"/>), as <c>(Key, Kind, Figure)</c> rows.</summary>
    public static HostInventorySql BuildWorkloadValues(KubernetesWorkloadKind kind, IReadOnlyList<string> workloadKeys, int windowMinutes, int bucketWidthSeconds, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("workloads", workloadKeys.ToArray());
        time.Parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);
        var keyExpr = WorkloadKeyExpr(kind);
        var where = $"{time.WhereSql} AND {keyExpr} IN {{workloads:Array(String)}}";

        return new HostInventorySql(
            BuildCounts(kind.Counts, keyExpr, where) + "UNION ALL\n" +
            BuildPodUsage(keyExpr, where) + "UNION ALL\n" +
            BuildPodCount(keyExpr, where),
            time.Parameters);
    }

    /// <summary>One workload's counts and pod usage per time bucket, as <c>(Key = BucketStart, Kind, Figure)</c> rows.</summary>
    public static HostInventorySql BuildWorkloadMetrics(KubernetesWorkloadKind kind, string workloadNamespace, string name, int windowMinutes, int bucketWidthSeconds, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("namespace", workloadNamespace);
        time.Parameters.AddParameter("name", name);
        time.Parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);
        var where = $"{time.WhereSql} AND {NamespaceExpr} = {{namespace:String}} AND {NameExpr(kind)} = {{name:String}}";

        return new HostInventorySql(
            BuildCounts(kind.Counts, BucketExpr, where) + "UNION ALL\n" + BuildPodUsage(BucketExpr, where),
            time.Parameters);
    }

    /// <summary>Distinct namespaces in the window with their last-seen time, filtered and capped.</summary>
    public static HostInventorySql BuildNamespaceList(KubernetesNamespaceListRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("namespaceLimit", (uint)(MaxNamespaces + 1));

        var extra = "";
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            extra += " AND positionCaseInsensitiveUTF8(Namespace, {search:String}) > 0";
        }

        string Branch(string table) =>
            $"  SELECT {NamespaceExpr} AS Namespace, max(Time) AS LastSeen\n" +
            $"  FROM {table}\n" +
            $"  WHERE MetricName LIKE 'k8s.%' AND {time.WhereSql} AND Namespace != ''{extra}\n" +
            "  GROUP BY Namespace\n";

        var sql = "SELECT Namespace, max(LastSeen) AS LatestSeen\n" +
            "FROM (\n" +
            Branch("metrics_gauge") +
            "  UNION ALL\n" +
            Branch("metrics_sum") +
            ")\n" +
            "GROUP BY Namespace\n" +
            "ORDER BY Namespace\n" +
            "LIMIT {namespaceLimit:UInt32}";

        return new HostInventorySql(sql, parameters);
    }

    /// <summary>Whole-window figures for <paramref name="namespaces"/>, as <c>(Key = namespace, Kind, Figure)</c> rows.</summary>
    public static HostInventorySql BuildNamespaceValues(IReadOnlyList<string> namespaces, int windowMinutes, int bucketWidthSeconds, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("namespaces", namespaces.ToArray());
        time.Parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);
        var where = $"{time.WhereSql} AND {NamespaceExpr} IN {{namespaces:Array(String)}}";

        return new HostInventorySql(
            BuildCounts(NamespaceCounts, NamespaceExpr, where) + "UNION ALL\n" +
            BuildPodUsage(NamespaceExpr, where) + "UNION ALL\n" +
            BuildPodCount(NamespaceExpr, where),
            time.Parameters);
    }

    /// <summary>Distinct pod volumes in the window with their type, claim and last-seen time, filtered and capped.</summary>
    public static HostInventorySql BuildVolumeList(KubernetesVolumeListRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("volumeLimit", (uint)(MaxVolumes + 1));

        var extra = "";
        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            parameters.AddParameter("namespace", request.Namespace.Trim());
            extra += " AND Namespace = {namespace:String}";
        }

        // Search applies to the resolved claim name (HAVING), not per row - same reasoning as
        // BuildPodList's node filter: a row without the claim attribute mustn't drop the volume.
        var having = "";
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            having = "HAVING positionCaseInsensitiveUTF8(Volume, {search:String}) > 0 OR positionCaseInsensitiveUTF8(LatestClaim, {search:String}) > 0\n";
        }

        string Branch(string table) =>
            $"  SELECT {NamespaceExpr} AS Namespace, {PodExpr} AS Pod, {VolumeExpr} AS Volume,\n" +
            "    ResourceAttributes['k8s.volume.type'] AS VolumeType,\n" +
            "    ResourceAttributes['k8s.persistentvolumeclaim.name'] AS Claim,\n" +
            "    max(Time) AS LastSeen\n" +
            $"  FROM {table}\n" +
            $"  WHERE MetricName LIKE 'k8s.volume.%' AND {time.WhereSql} AND Volume != ''{extra}\n" +
            "  GROUP BY Namespace, Pod, Volume, VolumeType, Claim\n";

        var sql = "SELECT Namespace, Pod, Volume,\n" +
            "  argMaxIf(VolumeType, LastSeen, VolumeType != '') AS LatestVolumeType,\n" +
            "  argMaxIf(Claim, LastSeen, Claim != '') AS LatestClaim,\n" +
            "  max(LastSeen) AS LatestSeen\n" +
            "FROM (\n" +
            Branch("metrics_gauge") +
            "  UNION ALL\n" +
            Branch("metrics_sum") +
            ")\n" +
            "GROUP BY Namespace, Pod, Volume\n" +
            having +
            "ORDER BY Namespace, Pod, Volume\n" +
            "LIMIT {volumeLimit:UInt32}";

        return new HostInventorySql(sql, parameters);
    }

    /// <summary>Latest capacity/available/inode readings for the volumes keyed by <paramref name="volumeKeys"/> (see <see cref="VolumeKey"/>).</summary>
    public static HostInventorySql BuildVolumeValues(IReadOnlyList<string> volumeKeys, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("volumes", volumeKeys.ToArray());

        return new HostInventorySql(
            BuildCounts(VolumeCounts, VolumeKeyExpr, $"{time.WhereSql} AND {VolumeKeyExpr} IN {{volumes:Array(String)}}"),
            time.Parameters);
    }

    /// <summary>One pod volume's latest readings per time bucket, as <c>(Key = BucketStart, Kind, Figure)</c> rows.</summary>
    public static HostInventorySql BuildVolumeMetrics(string volumeNamespace, string podName, string volumeName, int windowMinutes, int bucketWidthSeconds, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("namespace", volumeNamespace);
        time.Parameters.AddParameter("podName", podName);
        time.Parameters.AddParameter("volumeName", volumeName);
        time.Parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);
        var where = $"{time.WhereSql} AND {NamespaceExpr} = {{namespace:String}} AND {PodExpr} = {{podName:String}} AND {VolumeExpr} = {{volumeName:String}}";

        return new HostInventorySql(BuildCounts(VolumeCounts, BucketExpr, where), time.Parameters);
    }

    private const string BucketExpr = "toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND)";

    private static MetricFilterSql TimeFilter(int windowMinutes, DateTimeOffset now) =>
        MetricFilterSqlBuilder.Build(new MetricFilter { From = now - TimeSpan.FromMinutes(windowMinutes), To = now }, now);

    /// <summary>Each count's latest reading per <paramref name="keyExpr"/> (an object key for a list, a time bucket for a drill-down).</summary>
    private static string BuildCounts(IReadOnlyList<KubernetesWorkloadCount> counts, string keyExpr, string where)
    {
        static string InList(KubernetesWorkloadCount count) => string.Join(", ", count.MetricNames.Select(n => $"'{n}'"));

        // The WHERE admits only these metrics, so the last count needs no condition of its own.
        var kindExpr = counts.Count == 1
            ? $"'{counts[0].FigureKind}'"
            : "multiIf(\n" +
              string.Concat(counts.Take(counts.Count - 1).Select(c => $"        MetricName IN ({InList(c)}), '{c.FigureKind}',\n")) +
              $"        '{counts[^1].FigureKind}')";

        return $"SELECT Key, Kind, CAST(argMax(Value, Time) AS Nullable(Float64)) AS Figure\n" +
            "  FROM (\n" +
            $"    SELECT {keyExpr} AS Key, Time, Value, {kindExpr} AS Kind\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName IN ({string.Join(", ", counts.Select(InList))}) AND {where}\n" +
            "  )\n" +
            "  GROUP BY Key, Kind\n";
    }

    /// <summary>
    /// CPU/memory summed over pods per bucket, then averaged per <paramref name="keyExpr"/> -
    /// see the class remarks. With a bucket as the key, each group is one bucket and the
    /// average is just that bucket's total.
    /// </summary>
    private static string BuildPodUsage(string keyExpr, string where) =>
        $"SELECT Key, Kind, CAST(avg(Total) AS Nullable(Float64)) AS Figure\n" +
        "  FROM (\n" +
        "    SELECT Key, Bucket, Kind, sum(PodAverage) AS Total\n" +
        "    FROM (\n" +
        $"      SELECT {keyExpr} AS Key, {BucketExpr} AS Bucket, {PodExpr} AS Pod,\n" +
        $"        if(MetricName = 'k8s.pod.memory.working_set', '{MemoryKind}', '{CpuKind}') AS Kind,\n" +
        "        avg(Value) AS PodAverage\n" +
        "      FROM metrics_gauge\n" +
        $"      WHERE MetricName IN ({PodCpuMetrics}, 'k8s.pod.memory.working_set') AND {where}\n" +
        "      GROUP BY Key, Bucket, Pod, Kind\n" +
        "    )\n" +
        "    GROUP BY Key, Bucket, Kind\n" +
        "  )\n" +
        "  GROUP BY Key, Kind\n";

    /// <summary>Distinct pods seen on a <c>k8s.pod.*</c> gauge per <paramref name="keyExpr"/> in the window.</summary>
    private static string BuildPodCount(string keyExpr, string where) =>
        $"SELECT Key, '{PodCountKind}' AS Kind, CAST(uniqExact(Pod) AS Nullable(Float64)) AS Figure\n" +
        "  FROM (\n" +
        $"    SELECT {keyExpr} AS Key, {PodExpr} AS Pod\n" +
        "    FROM metrics_gauge\n" +
        $"    WHERE MetricName LIKE 'k8s.pod.%' AND {where} AND Pod != ''\n" +
        "  )\n" +
        "  GROUP BY Key\n";
}
