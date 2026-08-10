namespace Flare.Identity.Auth;

public interface ISessionStore
{
    /// <summary>Creates a new session for <paramref name="userId"/>, generating a fresh
    /// 256-bit token as its id. <paramref name="lifetime"/> sets a fixed
    /// <see cref="Session.ExpiresAt"/> from now - see <see cref="AuthOptions.SessionLifetime"/>.</summary>
    Task<Session> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>Returns the session for <paramref name="token"/>, or null if it doesn't
    /// exist or has expired (an expired row is lazily deleted on lookup rather than
    /// requiring a separate cleanup sweep to run first).</summary>
    Task<Session?> FindAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Revokes a single session (logout).</summary>
    Task DeleteAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Revokes every session for a user (e.g. when an admin disables the account).</summary>
    Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Bumps <see cref="Session.LastSeenAt"/> to now. Callers should throttle
    /// how often this is invoked per session (e.g. at most once a minute) - it's for an
    /// admin-facing "last active" display only, not for computing expiry.</summary>
    Task TouchLastSeenAsync(string token, CancellationToken cancellationToken = default);
}
