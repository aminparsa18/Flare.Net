namespace Flare.Ingest.Pipeline;

/// <summary>
/// Computes a per-process Redis Streams consumer name shared by all three pipelines'
/// options types (<see cref="LogEventPipelineOptions.ConsumerName"/>,
/// <see cref="SpanEventPipelineOptions.ConsumerName"/>,
/// <see cref="MetricEventPipelineOptions.ConsumerName"/>).
/// </summary>
/// <remarks>
/// Replaces the old hardcoded literals (<c>"flare-ingest-1"</c> and friends), which were
/// fine for v1's single-instance deployment model but would make two concurrent
/// <c>Flare.Ingest</c> replicas falsely reclaim each other's in-flight work under
/// XREADGROUP/XCLAIM (see Planning.md's "Multi-node scaling" item). <see cref="Environment.MachineName"/>
/// is already unique per container/pod in practice (Docker assigns each container a
/// unique hostname by default; Kubernetes pod names are unique within a Deployment), and
/// <see cref="Environment.ProcessId"/> is appended as extra safety in case two processes
/// ever do share a hostname (e.g. a host-network deployment). A name that changes across
/// restarts is safe: <c>ClickHouseFlushWorker</c>/<c>SpanFlushWorker</c>/
/// <c>MetricFlushWorker</c>'s reclaim loop scans the whole consumer group's pending-entries
/// list via XPENDING (not filtered by consumer name) and reclaims via XCLAIM under the
/// *current* consumer name, so entries left pending under a since-restarted process's old
/// name are picked up automatically once idle past <c>ReclaimIdle</c>.
/// </remarks>
internal static class ConsumerIdentity
{
    /// <summary>
    /// This process's default per-signal consumer name suffix - each pipeline's
    /// <c>ConsumerName</c> default prefixes this with its own signal-specific prefix
    /// (e.g. <c>"flare-ingest-"</c>, <c>"flare-ingest-spans-"</c>) so consumer names stay
    /// recognizable in <c>XINFO CONSUMERS</c> output while still being unique per process.
    /// </summary>
    public static readonly string Suffix = $"{Environment.MachineName}-{Environment.ProcessId}";
}
