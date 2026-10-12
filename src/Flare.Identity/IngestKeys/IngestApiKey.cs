namespace Flare.Identity.IngestKeys;

/// <summary>A named ingest API key. Never carries the raw key value - only
/// <see cref="DbIngestApiKeyStore.CreateAsync"/> ever sees the raw value, and only
/// at creation time, matching standard API-key UX (shown once, never retrievable again).</summary>
public sealed record IngestApiKey(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public bool IsActive => RevokedAt is null;

    /// <summary>When the key stops working (ADR-0207); null = never. Distinct from <see cref="RevokedAt"/>: an
    /// expired key is not revoked and can be given a new expiry (or none) to bring it back.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } at && at <= now;

    /// <summary>Not revoked and not expired - what "active" means to a person looking at the key list.</summary>
    public bool IsUsable(DateTimeOffset now) => IsActive && !IsExpired(now);

    public IngestApiKeyLimits Limits { get; init; } = IngestApiKeyLimits.None;

    /// <summary>The owning project (ADR-0123), or null for an instance-wide key.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Browser origins allowed to use this key (ADR-0149); empty = unrestricted.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>service.name values this key may write for (ADR-0150); empty = any.</summary>
    public IReadOnlyList<string> AllowedServices { get; init; } = [];
}

/// <summary>What <c>Flare.Ingest</c>'s validation cache needs per active key: the hash to
/// match a presented key against, plus the id/name/limits to enforce and attribute usage
/// under (ADR-0051).</summary>
public sealed record ActiveIngestApiKey(Guid Id, string Name, string KeyHash, IngestApiKeyLimits Limits)
{
    /// <summary>Checked per request against the clock, not just at cache refresh, so a key stops working at its
    /// expiry rather than up to a refresh interval later (ADR-0207).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    public IReadOnlyList<string> AllowedServices { get; init; } = [];
}
