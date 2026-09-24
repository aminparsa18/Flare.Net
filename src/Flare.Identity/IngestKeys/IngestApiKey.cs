namespace Flare.Identity.IngestKeys;

/// <summary>A named ingest API key. Never carries the raw key value - only
/// <see cref="SqliteIngestApiKeyStore.CreateAsync"/> ever sees the raw value, and only
/// at creation time, matching standard API-key UX (shown once, never retrievable again).</summary>
public sealed record IngestApiKey(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public bool IsActive => RevokedAt is null;

    public IngestApiKeyLimits Limits { get; init; } = IngestApiKeyLimits.None;
}

/// <summary>What <c>Flare.Ingest</c>'s validation cache needs per active key: the hash to
/// match a presented key against, plus the id/name/limits to enforce and attribute usage
/// under (ADR-0051).</summary>
public sealed record ActiveIngestApiKey(Guid Id, string Name, string KeyHash, IngestApiKeyLimits Limits);
