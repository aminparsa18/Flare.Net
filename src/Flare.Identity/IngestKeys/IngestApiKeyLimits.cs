namespace Flare.Identity.IngestKeys;

/// <summary>
/// Optional per-key ingestion caps (ADR-0051), enforced by <c>Flare.Ingest</c>'s
/// <c>IngestApiKeyValidationMiddleware</c> against Redis fixed-window counters - one UTC
/// minute window and one UTC day window. Each cap is independently optional (null = no
/// cap on that dimension); <see cref="Enabled"/> is a separate toggle so enforcement can be
/// switched off without losing the configured numbers.
/// </summary>
public sealed record IngestApiKeyLimits(
    bool Enabled,
    long? MaxEventsPerMinute,
    long? MaxBytesPerMinute,
    long? MaxEventsPerDay,
    long? MaxBytesPerDay)
{
    public static readonly IngestApiKeyLimits None = new(false, null, null, null, null);

    /// <summary>True only when enforcement is on <em>and</em> at least one cap is set -
    /// the ingest hot path skips the Redis usage read entirely otherwise.</summary>
    public bool IsEnforced =>
        Enabled && (MaxEventsPerMinute is not null || MaxBytesPerMinute is not null || MaxEventsPerDay is not null || MaxBytesPerDay is not null);
}
