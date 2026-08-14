namespace Flare.Identity.Users;

/// <summary>
/// A user account - local, Entra-provisioned, or Active Directory-provisioned.
/// Deliberately carries no <c>PasswordHash</c> - that stays internal to
/// <see cref="SqliteUserStore"/>, reachable only through
/// <see cref="IUserStore.VerifyPasswordAsync"/>, so it can never leak into an API
/// response by accident.
/// </summary>
/// <param name="AuthProvider">"Local", "Entra", or "ActiveDirectory" (see
/// <c>Migrations/0005_ldap_id.sql</c>'s CHECK constraint for the authoritative list).</param>
/// <param name="ExternalId">The Entra <c>oid</c> claim, or the AD <c>objectGUID</c>, for
/// an externally-provisioned account; null for "Local".</param>
public sealed record User(
    Guid Id,
    string Username,
    UserRole Role,
    DateTimeOffset CreatedAt,
    bool IsDisabled,
    string AuthProvider = "Local",
    string? ExternalId = null);
