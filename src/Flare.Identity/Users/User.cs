namespace Flare.Identity.Users;

/// <summary>
/// A user account, local or Entra-provisioned. Deliberately carries no
/// <c>PasswordHash</c> - that stays internal to <see cref="SqliteUserStore"/>, reachable
/// only through <see cref="IUserStore.VerifyPasswordAsync"/>, so it can never leak into an
/// API response by accident.
/// </summary>
/// <param name="AuthProvider">"Local" (the only value that exists pre-Entra) or "Entra".</param>
/// <param name="ExternalId">The Entra <c>oid</c> claim for an Entra-provisioned account;
/// null for "Local".</param>
public sealed record User(
    Guid Id,
    string Username,
    UserRole Role,
    DateTimeOffset CreatedAt,
    bool IsDisabled,
    string AuthProvider = "Local",
    string? ExternalId = null);
