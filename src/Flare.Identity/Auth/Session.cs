namespace Flare.Identity.Auth;

/// <summary>An active login. <see cref="Id"/> is the SHA-256 digest of the session token (see
/// <see cref="SessionTokenHasher"/>) and this row's primary key, so deleting the row revokes the session
/// immediately. <see cref="Token"/> is the raw cookie value and is only set on the instance returned by
/// <see cref="ISessionStore.CreateAsync"/> - it is never stored or recoverable afterwards.</summary>
public sealed record Session(string Id, Guid UserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset LastSeenAt, string? Token = null);
