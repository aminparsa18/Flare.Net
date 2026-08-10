namespace Flare.Identity.Auth;

/// <summary>An active login. <see cref="Id"/> is the opaque token itself - it doubles as
/// both the session cookie's value and this row's primary key (see
/// Migrations/0001_identity.sql), so deleting the row revokes the session immediately.</summary>
public sealed record Session(string Id, Guid UserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset LastSeenAt);
