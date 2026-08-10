using Microsoft.AspNetCore.Http;

namespace Flare.Identity.Auth;

/// <summary>Session cookie tuning, bound from the <c>Auth</c> configuration section.</summary>
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
}
