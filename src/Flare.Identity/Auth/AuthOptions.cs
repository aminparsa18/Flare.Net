using Microsoft.AspNetCore.Http;

namespace Flare.Identity.Auth;

/// <summary>Session cookie and local-login-lockout tuning, bound from the <c>Auth</c>
/// configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string CookieName { get; set; } = "flare_session";

    /// <summary>Fixed absolute expiry set at login - no sliding window, no separate
    /// refresh-token dance. See docs/auth.md for why (single-process SQLite deployment,
    /// server-side revocation already required, cookie transport already needed for the
    /// live-tail WebSocket).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Set false only for local plain-HTTP dev - every real deployment should
    /// run behind TLS and keep this true.</summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>Lax by default: dashboard and API are different ports (and possibly
    /// different registrable domains in some deployments) even in docker-compose - Lax
    /// still sends the cookie for the same-site case (e.g. both on `localhost`).
    /// Deployments that split dashboard/API across genuinely different domains need
    /// None (which also requires CookieSecure=true, per the cookie spec).</summary>
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.Lax;

    /// <summary>Consecutive failed local-login attempts for the same (username, client
    /// IP) pair - see <see cref="ILoginAttemptStore"/> - before that pair is locked out
    /// for <see cref="LoginLockoutDuration"/>. Only local username/password login is
    /// throttled this way; Entra/AD/OIDC/reverse-proxy each delegate credential
    /// verification elsewhere and aren't brute-forceable through this endpoint.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>How long a (username, client IP) pair stays locked out once
    /// <see cref="MaxFailedLoginAttempts"/> is reached.</summary>
    public TimeSpan LoginLockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>A failed attempt older than this resets the streak back to zero instead
    /// of counting toward <see cref="MaxFailedLoginAttempts"/> - without it, an
    /// occasional mistyped password over weeks/months would eventually accumulate into a
    /// lockout even though no actual attack was happening.</summary>
    public TimeSpan LoginFailureWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Requests a single personal access token may make within
    /// <see cref="PatRateLimitWindow"/> before <c>Flare.Api</c>'s rate limiter (see
    /// <c>Program.cs</c>'s <c>"PatRateLimit"</c> policy) starts returning 429s for it. Only
    /// PAT-authenticated requests are counted - cookie/session (dashboard) traffic is
    /// never subject to this. 120/minute is generous for CI/script use while still
    /// bounding a runaway loop or leaked token.</summary>
    public int PatRateLimitPermitLimit { get; set; } = 120;

    /// <summary>Fixed window <see cref="PatRateLimitPermitLimit"/> is measured over.</summary>
    public TimeSpan PatRateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
}
