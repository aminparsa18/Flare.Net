using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/kubernetes/workloads</c> - the Kubernetes page's Workloads
/// table for one <see cref="Kind"/>: one row per (<c>k8s.namespace.name</c>, workload name)
/// pair found on the <c>k8s_cluster</c> receiver's <c>k8s.&lt;kind&gt;.*</c> metrics or on a
/// <c>k8s.pod.*</c> metric carrying that workload's name attribute - see
/// <see cref="Query.KubernetesWorkloadQueryBuilder"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesWorkloadListRequest
{
    /// <summary><c>Deployment</c>, <c>StatefulSet</c>, <c>DaemonSet</c>, <c>Job</c> or <c>CronJob</c> (case-insensitive); anything else is a 400.</summary>
    public required string Kind { get; init; }

    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against the workload's name. Null/empty = no filter.</summary>
    public string? Search { get; init; }

    /// <summary>Exact match against <c>k8s.namespace.name</c>. Null/empty = all namespaces.</summary>
    public string? Namespace { get; init; }
}

/// <summary>
/// One workload's row in <see cref="KubernetesWorkloadListResponse"/>. The replica/job
/// counts are the latest <c>k8s_cluster</c> readings in the window and only the ones the
/// kind reports are ever set (see <see cref="Query.KubernetesWorkloadQueryBuilder"/>'s
/// remarks for which metric feeds which field per kind); pod count and usage come from
/// <c>kubeletstats</c>' pod metrics, so they need the <c>k8sattributes</c> processor to tag
/// pods with their workload. Null = no data, never silently 0.
/// </summary>
[MemoryPackable]
public sealed partial record KubernetesWorkloadSummary
{
    public required string Name { get; init; }

    public required string Namespace { get; init; }

    /// <summary>Desired replicas (Deployment/StatefulSet), desired scheduled nodes (DaemonSet), desired successful pods (Job).</summary>
    public int? Desired { get; init; }

    /// <summary>Available replicas (Deployment), ready pods (StatefulSet), ready nodes (DaemonSet).</summary>
    public int? Ready { get; init; }

    /// <summary>Current pods (StatefulSet), currently scheduled nodes (DaemonSet).</summary>
    public int? Current { get; init; }

    /// <summary>Active pods (Job), active jobs (CronJob).</summary>
    public int? Active { get; init; }

    /// <summary>Successful pods (Job).</summary>
    public int? Succeeded { get; init; }

    /// <summary>Failed pods (Job).</summary>
    public int? Failed { get; init; }

    /// <summary>Distinct pods that reported a <c>k8s.pod.*</c> metric carrying this workload's name within the window.</summary>
    public int? PodCount { get; init; }

    /// <summary>Summed over the workload's pods per time bucket, then averaged over the buckets - see <see cref="Query.KubernetesWorkloadQueryBuilder"/>.</summary>
    public double? CpuCores { get; init; }

    public double? MemoryWorkingSetBytes { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/workloads</c>, sorted by namespace then name.</summary>
[MemoryPackable]
public sealed partial record KubernetesWorkloadListResponse
{
    /// <summary>The request's kind, normalized to its canonical casing.</summary>
    public required string Kind { get; init; }

    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<KubernetesWorkloadSummary> Workloads { get; init; }

    /// <summary>True when more workloads matched than <see cref="Query.KubernetesWorkloadQueryBuilder.MaxWorkloads"/>.</summary>
    public required bool Truncated { get; init; }
}

/// <summary>Request body for <c>POST /api/kubernetes/workloads/metrics</c> - one workload's drill-down charts.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesWorkloadMetricsRequest
{
    public required string Kind { get; init; }

    public required string Namespace { get; init; }

    public required string Name { get; init; }

    public int? WindowMinutes { get; init; }
}

/// <summary>One time bucket of <see cref="KubernetesWorkloadMetricsResponse"/> - same fields and null semantics as <see cref="KubernetesWorkloadSummary"/>, scoped to the bucket (counts are the bucket's latest reading).</summary>
[MemoryPackable]
public sealed partial record KubernetesWorkloadMetricsPoint
{
    public required DateTimeOffset BucketStart { get; init; }

    public int? Desired { get; init; }

    public int? Ready { get; init; }

    public int? Current { get; init; }

    public int? Active { get; init; }

    public int? Succeeded { get; init; }

    public int? Failed { get; init; }

    public double? CpuCores { get; init; }

    public double? MemoryWorkingSetBytes { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/workloads/metrics</c>, <see cref="Points"/> ascending by bucket; buckets with no data are absent.</summary>
[MemoryPackable]
public sealed partial record KubernetesWorkloadMetricsResponse
{
    public required string Kind { get; init; }

    public required string Namespace { get; init; }

    public required string Name { get; init; }

    public required int WindowMinutes { get; init; }

    public required int BucketWidthSeconds { get; init; }

    public required IReadOnlyList<KubernetesWorkloadMetricsPoint> Points { get; init; }
}

/// <summary>Request body for <c>POST /api/kubernetes/namespaces</c> - one row per <c>k8s.namespace.name</c> found on any ingested <c>k8s.*</c> metric.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesNamespaceListRequest
{
    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against the namespace name. Null/empty = no filter.</summary>
    public string? Search { get; init; }
}

/// <summary>One namespace's row in <see cref="KubernetesNamespaceListResponse"/> - pod count and usage aggregated the same way as <see cref="KubernetesWorkloadSummary"/>'s.</summary>
[MemoryPackable]
public sealed partial record KubernetesNamespaceSummary
{
    public required string Namespace { get; init; }

    /// <summary><c>Active</c>/<c>Terminating</c>, decoded from the latest <c>k8s.namespace.phase</c> (1/0). Null when not reported.</summary>
    public string? Phase { get; init; }

    public int? PodCount { get; init; }

    public double? CpuCores { get; init; }

    public double? MemoryWorkingSetBytes { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/namespaces</c>, sorted by name.</summary>
[MemoryPackable]
public sealed partial record KubernetesNamespaceListResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<KubernetesNamespaceSummary> Namespaces { get; init; }

    public required bool Truncated { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/kubernetes/volumes</c> - one row per (namespace, pod,
/// <c>k8s.volume.name</c>) found on <c>kubeletstats</c>' <c>k8s.volume.*</c> metrics (its
/// opt-in <c>volume</c> metric group).
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesVolumeListRequest
{
    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against the volume name or its persistent volume claim's name. Null/empty = no filter.</summary>
    public string? Search { get; init; }

    /// <summary>Exact match against <c>k8s.namespace.name</c>. Null/empty = all namespaces.</summary>
    public string? Namespace { get; init; }
}

/// <summary>
/// One pod volume's row in <see cref="KubernetesVolumeListResponse"/>. Sizes are the latest
/// readings in the window (a fill level, not a rate - averaging would hide a volume filling
/// up). <see cref="UsedBytes"/>/<see cref="UsedPercent"/> are derived from capacity and
/// available, so they need both.
/// </summary>
[MemoryPackable]
public sealed partial record KubernetesVolumeSummary
{
    public required string VolumeName { get; init; }

    public required string Namespace { get; init; }

    public required string PodName { get; init; }

    /// <summary>The <c>k8s.volume.type</c> resource attribute (<c>persistentVolumeClaim</c>, <c>emptyDir</c>, <c>configMap</c>, ...).</summary>
    public string? VolumeType { get; init; }

    /// <summary>The <c>k8s.persistentvolumeclaim.name</c> resource attribute, for a PVC-backed volume.</summary>
    public string? ClaimName { get; init; }

    public double? CapacityBytes { get; init; }

    public double? AvailableBytes { get; init; }

    public double? UsedBytes { get; init; }

    public double? UsedPercent { get; init; }

    /// <summary>Used inodes as a percentage of total inodes.</summary>
    public double? InodesUsedPercent { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/volumes</c>, sorted by namespace, pod, then volume name.</summary>
[MemoryPackable]
public sealed partial record KubernetesVolumeListResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<KubernetesVolumeSummary> Volumes { get; init; }

    public required bool Truncated { get; init; }
}

/// <summary>Request body for <c>POST /api/kubernetes/volumes/metrics</c> - one pod volume's drill-down charts.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesVolumeMetricsRequest
{
    public required string Namespace { get; init; }

    public required string PodName { get; init; }

    public required string VolumeName { get; init; }

    public int? WindowMinutes { get; init; }
}

/// <summary>One time bucket of <see cref="KubernetesVolumeMetricsResponse"/> - the bucket's latest capacity/available readings and what they derive.</summary>
[MemoryPackable]
public sealed partial record KubernetesVolumeMetricsPoint
{
    public required DateTimeOffset BucketStart { get; init; }

    public double? UsedBytes { get; init; }

    public double? UsedPercent { get; init; }

    public double? InodesUsedPercent { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/volumes/metrics</c>, <see cref="Points"/> ascending by bucket.</summary>
[MemoryPackable]
public sealed partial record KubernetesVolumeMetricsResponse
{
    public required string Namespace { get; init; }

    public required string PodName { get; init; }

    public required string VolumeName { get; init; }

    public required int WindowMinutes { get; init; }

    public required int BucketWidthSeconds { get; init; }

    public required IReadOnlyList<KubernetesVolumeMetricsPoint> Points { get; init; }
}
