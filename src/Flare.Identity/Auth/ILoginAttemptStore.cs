namespace Flare.Identity.Auth;

/// <summary>Failed-password-attempt throttling for local login, keyed by
/// (username, client IP) - see <c>Migrations/0014_login_attempts.sql</c> for why the
/// composite key instead of username alone. Every method is a no-op past the point
/// <see cref="AuthOptions.LocalEnabled"/>-style gating would apply; callers only need
/// this for the local username/password path, not Entra/AD/OIDC/reverse-proxy.</summary>
public interface ILoginAttemptStore
{
    /// <summary>Returns the lockout expiry if (<paramref name="username"/>,
    /// <paramref name="clientIp"/>) is currently locked out, or <c>null</c> if it isn't
    /// (never failed, failed below the threshold, or a previous lockout has expired -
    /// implementations lazily clear an expired lockout on this call, same convention as
    /// <see cref="ISessionStore.FindAsync"/> reaping an expired session).</summary>
    Task<DateTimeOffset?> GetLockedUntilAsync(string username, string clientIp, CancellationToken cancellationToken = default);

    /// <summary>Records a failed password attempt. Once the failure count within the
    /// configured window reaches <see cref="AuthOptions.MaxFailedLoginAttempts"/>, locks
    /// the pair out for <see cref="AuthOptions.LoginLockoutDuration"/>.</summary>
    Task RecordFailureAsync(string username, string clientIp, CancellationToken cancellationToken = default);

    /// <summary>Clears any tracked failures for (<paramref name="username"/>,
    /// <paramref name="clientIp"/>) - called on a successful login so the next mistyped
    /// password starts counting from zero again, not from wherever a prior, unrelated
    /// streak of failures left off.</summary>
    Task RecordSuccessAsync(string username, string clientIp, CancellationToken cancellationToken = default);
}
