namespace Flare.Identity.Apdex;

/// <summary>
/// Per-service Apdex threshold (T, milliseconds) overrides for the Traces > Services tab
/// - see docs-internal/adr/0032-apdex-score-per-service.md. Only overrides are stored; a
/// service with no row here uses <c>Flare.Api</c>'s fixed default.
/// </summary>
public interface IApdexThresholdStore
{
    /// <summary>Every configured override, keyed by <c>ServiceName</c>. Returns the whole
    /// map rather than a per-service lookup - callers (<c>ServiceApdexQueryBuilder</c>)
    /// always need every override at once to build the query's per-service threshold
    /// expression, and the row count here is naturally small (one per service someone has
    /// actually configured).</summary>
    Task<IReadOnlyDictionary<string, int>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts the threshold override for one service.</summary>
    Task SetAsync(string serviceName, int thresholdMs, CancellationToken cancellationToken = default);

    /// <summary>Removes a service's override, reverting it to the default threshold. A
    /// no-op if no override exists.</summary>
    Task ResetAsync(string serviceName, CancellationToken cancellationToken = default);
}
