namespace Flare.Identity.IngestKeys;

/// <summary>
/// Optional per-key ingestion caps (ADR-0051), enforced by <c>Flare.Ingest</c>'s
/// <c>IngestApiKeyValidationMiddleware</c> against Redis fixed-window counters - one UTC
/// minute window and one UTC day window. Each cap is independently optional (null = no
/// cap on that dimension); <see cref="Enabled"/> is a separate toggle so enforcement can be
/// switched off without losing the configured numbers.
/// </summary>
/// <remarks>
/// The two replay caps (ADR-0268) bound what a key may upload to <c>POST /v1/replay</c> and
/// nothing else. A replay chunk still counts against the byte caps above as well, so they
/// are an extra ceiling on one kind of traffic, not a separate budget.
/// </remarks>
public sealed record IngestApiKeyLimits(
    bool Enabled,
    long? MaxEventsPerMinute,
    long? MaxBytesPerMinute,
    long? MaxEventsPerDay,
    long? MaxBytesPerDay,
    long? MaxReplayBytesPerMinute = null,
    long? MaxReplayBytesPerDay = null)
{
    public static readonly IngestApiKeyLimits None = new(false, null, null, null, null);

    /// <summary>True only when enforcement is on <em>and</em> at least one cap is set -
    /// the ingest hot path skips the Redis usage read entirely otherwise.</summary>
    public bool IsEnforced =>
        Enabled && (MaxEventsPerMinute is not null || MaxBytesPerMinute is not null || MaxEventsPerDay is not null || MaxBytesPerDay is not null
            || MaxReplayBytesPerMinute is not null || MaxReplayBytesPerDay is not null);
}
