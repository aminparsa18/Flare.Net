namespace Flare.Api.Auth;

/// <summary>Scheme name constants for the Microsoft Entra ID (SSO) login path -
/// deliberately separate from <see cref="Flare.Identity.Auth.SessionAuthenticationDefaults"/>
/// since these two schemes are only ever paired together during the OIDC handshake
/// itself, never used to authenticate an ordinary API request (see docs/auth.md - the
/// <c>flare_session</c> cookie/<c>SessionAuthenticationHandler</c> stays the only scheme
/// any endpoint's <c>RequireAuthorization()</c> ever actually resolves a principal
/// through).</summary>
public static class EntraAuthenticationDefaults
{
    /// <summary>The <c>AddOpenIdConnect()</c> scheme name, challenged by
    /// <c>GET /api/auth/entra/login</c>.</summary>
    public const string SchemeName = "Entra";

    /// <summary>A short-lived, single-use cookie scheme that exists only to carry the
    /// OIDC handler's validated <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// across the redirect back from Microsoft to <c>GET /api/auth/entra/complete</c> -
    /// never the app's real session. Flare mints its own <c>flare_session</c> cookie
    /// there and immediately signs this one back out.</summary>
    public const string ExternalCookieScheme = "EntraExternal";
}
