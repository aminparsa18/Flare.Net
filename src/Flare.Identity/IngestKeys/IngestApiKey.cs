namespace Flare.Identity.IngestKeys;

/// <summary>A named ingest API key. Never carries the raw key value - only
/// <see cref="SqliteIngestApiKeyStore.CreateAsync"/> ever sees the raw value, and only
/// at creation time, matching standard API-key UX (shown once, never retrievable again).</summary>
public sealed record IngestApiKey(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public bool IsActive => RevokedAt is null;
}
