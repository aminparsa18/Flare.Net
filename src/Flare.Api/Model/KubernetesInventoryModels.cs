using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/kubernetes/nodes</c> - the Kubernetes page's Nodes table:
/// one row per <c>k8s.node.name</c> resource attribute found on ingested <c>k8s.node.*</c>
/// metrics (the collector's <c>kubeletstats</c>/<c>k8s_cluster</c> receivers) - see
/// <see cref="Query.KubernetesInventoryQueryBuilder"/>.
/// </summary>
/// <remarks>
/// Same generatable shape as <see cref="HostListRequest"/> (relative window, single-value
/// filters, no <c>DateTimeOffset</c>/list member), so this carries <c>[GenerateTypeScript]</c>.
/// </remarks>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesNodeListRequest
{
    /// <summary>Lookback window; same default/clamp as <see cref="HostListRequest.WindowMinutes"/>.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against <c>k8s.node.name</c>. Null/empty = no filter.</summary>
    public string? Search { get; init; }

    /// <summary>Exact match against the <c>k8s.cluster.name</c> resource attribute. Null/empty = all clusters.</summary>
    public string? ClusterName { get; init; }
}

/// <summary>
/// One node's row in <see cref="KubernetesNodeListResponse"/>. Every figure is averaged over
/// the request window (or, for <see cref="Ready"/>, the latest reading in it) and null when
/// the node sent no data for it - never silently 0. See
/// <see cref="Query.KubernetesInventoryQueryBuilder"/>'s remarks for which metric feeds which field.
/// </summary>
[MemoryPackable]
public sealed partial record KubernetesNodeSummary
{
    public required string NodeName { get; init; }

    /// <summary>The <c>k8s.cluster.name</c> resource attribute, null when no data point carried one.</summary>
    public string? ClusterName { get; init; }

    /// <summary>The latest <c>k8s.node.condition_ready</c> reading: true/false, null when it's unknown (-1) or not reported.</summary>
    public bool? Ready { get; init; }

    public double? CpuCores { get; init; }

    /// <summary><see cref="CpuCores"/> as a percentage of the node's allocatable CPU - null unless the <c>k8s_cluster</c> receiver reports allocatable CPU.</summary>
    public double? CpuPercent { get; init; }

    public double? MemoryWorkingSetBytes { get; init; }

    /// <summary>Working set as a percentage of working set + available memory (the kubelet's own capacity split).</summary>
    public double? MemoryPercent { get; init; }

    /// <summary>Distinct pods that reported a <c>k8s.pod.*</c> metric carrying this node's name within the window.</summary>
    public int? PodCount { get; init; }

    /// <summary>Timestamp of the newest <c>k8s.node.*</c> data point this node sent within the window.</summary>
    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/nodes</c>, sorted by <see cref="KubernetesNodeSummary.NodeName"/>.</summary>
[MemoryPackable]
public sealed partial record KubernetesNodeListResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<KubernetesNodeSummary> Nodes { get; init; }

    /// <summary>True when more nodes matched than <see cref="Query.KubernetesInventoryQueryBuilder.MaxNodes"/>.</summary>
    public required bool Truncated { get; init; }
}

/// <summary>Request body for <c>POST /api/kubernetes/nodes/metrics</c> - one node's drill-down charts.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesNodeMetricsRequest
{
    public required string NodeName { get; init; }

    /// <summary>Same default/clamp as <see cref="KubernetesNodeListRequest.WindowMinutes"/>; the bucket width is derived from it server-side.</summary>
    public int? WindowMinutes { get; init; }
}

/// <summary>One time bucket of <see cref="KubernetesNodeMetricsResponse"/> - same per-field meaning and null semantics as <see cref="KubernetesNodeSummary"/>, scoped to the bucket.</summary>
[MemoryPackable]
public sealed partial record KubernetesNodeMetricsPoint
{
    public required DateTimeOffset BucketStart { get; init; }

    public double? CpuCores { get; init; }

    public double? CpuPercent { get; init; }

    public double? MemoryWorkingSetBytes { get; init; }

    public double? MemoryPercent { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/nodes/metrics</c>, <see cref="Points"/> ascending by bucket; buckets with no data are absent.</summary>
[MemoryPackable]
public sealed partial record KubernetesNodeMetricsResponse
{
    public required string NodeName { get; init; }

    public required int WindowMinutes { get; init; }

    public required int BucketWidthSeconds { get; init; }

    /// <summary>The latest allocatable CPU reading in the window - the denominator of every point's <see cref="KubernetesNodeMetricsPoint.CpuPercent"/>. Null when not reported.</summary>
    public double? AllocatableCpuCores { get; init; }

    public required IReadOnlyList<KubernetesNodeMetricsPoint> Points { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/kubernetes/pods</c> - the Kubernetes page's Pods table: one
/// row per (<c>k8s.namespace.name</c>, <c>k8s.pod.name</c>) pair found on ingested
/// <c>k8s.pod.*</c> metrics. A pod's drill-down reuses <c>POST /api/pods/metrics</c>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record KubernetesPodListRequest
{
    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against <c>k8s.pod.name</c>. Null/empty = no filter.</summary>
    public string? Search { get; init; }

    /// <summary>Exact match against <c>k8s.namespace.name</c>. Null/empty = all namespaces.</summary>
    public string? Namespace { get; init; }

    /// <summary>Exact match against the pod's latest <c>k8s.node.name</c>. Null/empty = every node (including pods with no node attribute).</summary>
    public string? NodeName { get; init; }

    /// <summary>
    /// With <see cref="WorkloadName"/>: only pods that carried this workload kind's name
    /// attribute (<c>k8s.deployment.name</c>, ...) with that value on any data point in the
    /// window - the Workloads tab's "View pods". Matches the attribute itself rather than the
    /// row's resolved <see cref="KubernetesPodSummary.WorkloadKind"/>, so a Job's pods are found
    /// even though a CronJob-owned pod resolves to its CronJob. Same kinds as
    /// <see cref="KubernetesWorkloadListRequest.Kind"/>; an unknown kind is ignored.
    /// </summary>
    public string? WorkloadKind { get; init; }

    public string? WorkloadName { get; init; }
}

/// <summary>
/// One pod's row in <see cref="KubernetesPodListResponse"/>. Usage figures are window
/// averages; <see cref="Phase"/>/<see cref="Restarts"/> are the latest readings. Every field
/// is null when the pod sent no data for it (phase and restarts come from the
/// <c>k8s_cluster</c> receiver, usage from <c>kubeletstats</c> - a cluster running only one
/// fills only its half).
/// </summary>
[MemoryPackable]
public sealed partial record KubernetesPodSummary
{
    public required string PodName { get; init; }

    public required string Namespace { get; init; }

    public string? NodeName { get; init; }

    /// <summary><c>Deployment</c>/<c>StatefulSet</c>/<c>DaemonSet</c>/<c>CronJob</c>/<c>Job</c>/<c>ReplicaSet</c> - the owning workload's kind, from whichever <c>k8s.*.name</c> resource attribute is set.</summary>
    public string? WorkloadKind { get; init; }

    public string? WorkloadName { get; init; }

    /// <summary><c>Pending</c>/<c>Running</c>/<c>Succeeded</c>/<c>Failed</c>/<c>Unknown</c>, decoded from <c>k8s.pod.phase</c>'s 1-5.</summary>
    public string? Phase { get; init; }

    /// <summary>Sum over the pod's containers of each one's latest <c>k8s.container.restarts</c>.</summary>
    public int? Restarts { get; init; }

    public double? CpuCores { get; init; }

    public double? MemoryWorkingSetBytes { get; init; }

    public double? CpuLimitPercent { get; init; }

    public double? MemoryLimitPercent { get; init; }

    /// <summary>Timestamp of the newest <c>k8s.pod.*</c> data point this pod sent within the window.</summary>
    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/kubernetes/pods</c>, sorted by namespace then pod name.</summary>
[MemoryPackable]
public sealed partial record KubernetesPodListResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<KubernetesPodSummary> Pods { get; init; }

    /// <summary>True when more pods matched than <see cref="Query.KubernetesInventoryQueryBuilder.MaxPods"/>.</summary>
    public required bool Truncated { get; init; }
}
