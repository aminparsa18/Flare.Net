namespace Flare.Identity.DashboardPins;

/// <summary>
/// A user's pinned dashboards (ADR-0089). Pinning is a per-user ordering preference, so it
/// lives in Identity rather than on the shared dashboard row.
/// </summary>
public interface IDashboardPinStore
{
    /// <summary>The dashboard ids <paramref name="userId"/> has pinned, most recently pinned first.</summary>
    Task<IReadOnlyList<Guid>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Pins a dashboard. A no-op if it is already pinned.</summary>
    Task PinAsync(Guid userId, Guid dashboardId, CancellationToken cancellationToken = default);

    /// <summary>Unpins a dashboard. A no-op if it isn't pinned.</summary>
    Task UnpinAsync(Guid userId, Guid dashboardId, CancellationToken cancellationToken = default);
}
