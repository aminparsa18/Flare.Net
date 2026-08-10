namespace Flare.Identity.Users;

/// <summary>
/// A local user account. Deliberately carries no <c>PasswordHash</c> - that stays
/// internal to <see cref="SqliteUserStore"/>, reachable only through
/// <see cref="IUserStore.VerifyPasswordAsync"/>, so it can never leak into an API
/// response by accident.
/// </summary>
public sealed record User(Guid Id, string Username, UserRole Role, DateTimeOffset CreatedAt, bool IsDisabled);
