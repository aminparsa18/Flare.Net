using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/pods/metrics</c> - one Kubernetes pod's CPU/memory over a
/// window, from the OTel <c>kubeletstats</c> receiver's pod metrics (see
/// <see cref="Query.PodMetricsQueryBuilder"/>). The log event detail view's pod charts.
/// </summary>
/// <remarks>
/// Same generatable shape as <see cref="HostMetricsRequest"/> (window + epoch-ms end, no
/// <c>DateTimeOffset</c>), so this carries <c>[GenerateTypeScript]</c>.
/// </remarks>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PodMetricsRequest
{
    /// <summary>The <c>k8s.pod.name</c> resource attribute.</summary>
    public required string PodName { get; init; }

    /// <summary>The <c>k8s.namespace.name</c> resource attribute. Null/empty = match the pod name in any namespace.</summary>
    public string? Namespace { get; init; }

    /// <summary>Same default/clamp as <see cref="HostMetricsRequest.WindowMinutes"/>.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Same meaning as <see cref="HostMetricsRequest.EndUnixMs"/>: where the window ends, null = now.</summary>
    public long? EndUnixMs { get; init; }
}

/// <summary>
/// One time bucket of <see cref="PodMetricsResponse"/>. Each figure is averaged over the
/// bucket and null when the pod sent no data for it - the two limit figures in particular
/// are opt-in <c>kubeletstats</c> metrics and are usually null.
/// </summary>
[MemoryPackable]
public sealed partial record PodMetricsPoint
{
    public required DateTimeOffset BucketStart { get; init; }

    /// <summary>CPU in use, in cores (<c>k8s.pod.cpu.usage</c>, or the older <c>k8s.pod.cpu.utilization</c> it replaced).</summary>
    public double? CpuCores { get; init; }

    /// <summary>Memory working set in bytes (<c>k8s.pod.memory.working_set</c>) - what the kubelet's eviction and the OOM killer act on.</summary>
    public double? MemoryWorkingSetBytes { get; init; }

    /// <summary>CPU use as a percentage of the pod's CPU limit (<c>k8s.pod.cpu_limit_utilization</c>, opt-in).</summary>
    public double? CpuLimitPercent { get; init; }

    /// <summary>Memory use as a percentage of the pod's memory limit (<c>k8s.pod.memory_limit_utilization</c>, opt-in).</summary>
    public double? MemoryLimitPercent { get; init; }
}

/// <summary>Response body for <c>POST /api/pods/metrics</c>, <see cref="Points"/> ascending by bucket. Buckets with no data are absent, not zero-filled.</summary>
[MemoryPackable]
public sealed partial record PodMetricsResponse
{
    public required string PodName { get; init; }

    public required int WindowMinutes { get; init; }

    public required int BucketWidthSeconds { get; init; }

    public required IReadOnlyList<PodMetricsPoint> Points { get; init; }
}
