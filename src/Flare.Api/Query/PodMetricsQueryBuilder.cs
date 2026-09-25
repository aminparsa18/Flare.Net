using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure SQL builder for <c>POST /api/pods/metrics</c>: one Kubernetes pod's CPU and memory
/// per time bucket, from the OTel <c>kubeletstats</c> receiver's pod-level gauges already in
/// <c>metrics_gauge</c>. No new table or ingest-side change - same approach as
/// <see cref="HostInventoryQueryBuilder"/>, whose window clamp, window-end resolution and
/// bucket width this reuses.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pod identity</b> is the <c>k8s.pod.name</c> resource attribute, optionally narrowed
/// by <c>k8s.namespace.name</c> - the same pair the collector's <c>k8sattributes</c>
/// processor puts on a pod's logs, so a log can be matched to its pod's metrics. A pod
/// name reused by a recreated pod (a StatefulSet's <c>web-0</c>) matches both instances;
/// within one short window that's the same workload slot, which is what the question is
/// about.
/// </para>
/// <para>
/// <b>Which metric each column comes from</b>:
/// <list type="bullet">
/// <item><b>CPU cores</b>: <c>k8s.pod.cpu.usage</c> (default-enabled in current
/// <c>kubeletstats</c>), or <c>k8s.pod.cpu.utilization</c> - the older, deprecated name for
/// the same cores figure, which older collectors still send.</item>
/// <item><b>Memory</b>: <c>k8s.pod.memory.working_set</c> bytes.</item>
/// <item><b>CPU/memory % of limit</b>: <c>k8s.pod.cpu_limit_utilization</c> /
/// <c>k8s.pod.memory_limit_utilization</c> (ratios, scaled to percent). Opt-in in
/// <c>kubeletstats</c> and only reported for pods with limits set, so usually absent.</item>
/// </list>
/// Everything is a gauge, averaged per bucket - no counter/reset handling needed.
/// </para>
/// </remarks>
public static class PodMetricsQueryBuilder
{
    public const string CpuKind = "cpu";
    public const string MemoryKind = "memory";
    public const string CpuLimitKind = "cpu_limit";
    public const string MemoryLimitKind = "memory_limit";

    /// <summary>One pod's figures per time bucket over the <paramref name="windowMinutes"/> ending at <paramref name="end"/>, as <c>(BucketStart, Kind, Value)</c> rows.</summary>
    public static HostInventorySql BuildPodMetrics(string podName, string? podNamespace, int windowMinutes, int bucketWidthSeconds, DateTimeOffset end)
    {
        var time = MetricFilterSqlBuilder.Build(new MetricFilter { From = end - TimeSpan.FromMinutes(windowMinutes), To = end }, end);
        var parameters = time.Parameters;
        parameters.AddParameter("podName", podName);
        parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);

        var podWhere = "ResourceAttributes['k8s.pod.name'] = {podName:String}";
        if (!string.IsNullOrWhiteSpace(podNamespace))
        {
            parameters.AddParameter("podNamespace", podNamespace.Trim());
            podWhere += " AND ResourceAttributes['k8s.namespace.name'] = {podNamespace:String}";
        }

        // One pass over metrics_gauge: MetricName IN (...) prunes by the table's leading
        // ORDER BY column, and multiIf folds the two CPU metric names into one kind.
        var sql =
            "SELECT BucketStart, Kind, CAST(avg(Scaled) AS Nullable(Float64)) AS Value\n" +
            "FROM (\n" +
            "  SELECT toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart,\n" +
            "    multiIf(\n" +
            $"      MetricName IN ('k8s.pod.cpu.usage', 'k8s.pod.cpu.utilization'), '{CpuKind}',\n" +
            $"      MetricName = 'k8s.pod.memory.working_set', '{MemoryKind}',\n" +
            $"      MetricName = 'k8s.pod.cpu_limit_utilization', '{CpuLimitKind}',\n" +
            $"      '{MemoryLimitKind}') AS Kind,\n" +
            "    if(endsWith(MetricName, '_limit_utilization'), 100 * Value, Value) AS Scaled\n" +
            "  FROM metrics_gauge\n" +
            "  WHERE MetricName IN ('k8s.pod.cpu.usage', 'k8s.pod.cpu.utilization', 'k8s.pod.memory.working_set',\n" +
            "      'k8s.pod.cpu_limit_utilization', 'k8s.pod.memory_limit_utilization')\n" +
            $"    AND {time.WhereSql} AND {podWhere}\n" +
            ")\n" +
            "GROUP BY BucketStart, Kind\n" +
            "ORDER BY BucketStart";

        return new HostInventorySql(sql, parameters);
    }
}
