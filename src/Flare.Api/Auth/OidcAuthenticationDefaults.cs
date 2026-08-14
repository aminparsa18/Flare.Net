namespace Flare.Api.Auth;

/// <summary>Scheme name constants for the generic OpenID Connect login path -
/// deliberately separate from <see cref="Flare.Identity.Auth.SessionAuthenticationDefaults"/>
/// (same reasoning as <see cref="EntraAuthenticationDefaults"/>: these two schemes are only
/// ever paired together during the OIDC handshake itself, never used to authenticate an
/// ordinary API request - the <c>flare_session</c> cookie/<c>SessionAuthenticationHandler</c>
/// stays the only scheme any endpoint's <c>RequireAuthorization()</c> ever actually resolves
/// a principal through).</summary>
public static class OidcAuthenticationDefaults
{
    /// <summary>The <c>AddOpenIdConnect()</c> scheme name, challenged by
    /// <c>GET /api/auth/oidc/login</c>.</summary>
    public const string SchemeName = "Oidc";

    /// <summary>A short-lived, single-use cookie scheme that exists only to carry the
    /// OIDC handler's validated <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// across the redirect back from the provider to <c>GET /api/auth/oidc/complete</c> -
    /// never the app's real session. Flare mints its own <c>flare_session</c> cookie
    /// there and immediately signs this one back out. Same pattern as
    /// <see cref="EntraAuthenticationDefaults.ExternalCookieScheme"/>.</summary>
    public const string ExternalCookieScheme = "OidcExternal";

    /// <summary>Explicit, distinct from Entra's callback path - <c>OpenIdConnectOptions</c>
    /// defaults every scheme's callback to <c>/signin-oidc</c> (the value
    /// <see cref="EntraAuthenticationDefaults.SchemeName"/>'s registration relies on
    /// implicitly), which would collide since both schemes are registered in the same
    /// app. Kept out of that default path entirely rather than relying on scheme-name
    /// disambiguation.</summary>
    public const string CallbackPath = "/signin-oidc-generic";
}
