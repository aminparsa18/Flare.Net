namespace Flare.Identity.PersonalAccessTokens;

/// <summary>A named personal access token belonging to a <see cref="Users.User"/>. Never
/// carries the raw token value - only <see cref="SqlitePersonalAccessTokenStore.CreateAsync"/>
/// ever sees the raw value, and only at creation time, matching
/// <see cref="IngestKeys.IngestApiKey"/>'s "shown once, never retrievable again" UX.</summary>
public sealed record PersonalAccessToken(
    Guid Id,
    Guid UserId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt)
{
    /// <summary>Active means not revoked and (if it has an expiry at all) not yet
    /// expired - takes <paramref name="now"/> explicitly rather than reading
    /// <see cref="DateTimeOffset.UtcNow"/> itself so callers go through the same
    /// <see cref="TimeProvider"/> the rest of the store does.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
