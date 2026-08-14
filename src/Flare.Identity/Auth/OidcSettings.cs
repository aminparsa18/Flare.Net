namespace Flare.Identity.Auth;

/// <summary>
/// Generic OpenID Connect settings as configured through the dashboard's Admin-only
/// <c>/auth</c> screen - see docs/auth.md's "OpenID Connect" section. Unlike
/// <see cref="EntraSettings"/> (which is hardcoded to Microsoft's authority URL
/// pattern), <see cref="Authority"/> points directly at whatever standards-compliant
/// provider (Okta, Auth0, Keycloak, Authentik, ...) the operator configures. Deliberately
/// carries the real <see cref="ClientSecret"/> - same reversibility requirement as
/// <see cref="EntraSettings.ClientSecret"/> (sent to the provider on every token
/// exchange) - so it never appears in an API response; see
/// <c>OidcSettingsEndpoints</c>'s <c>hasClientSecret</c> boolean instead.
/// </summary>
public sealed record OidcSettings(
    bool Enabled,
    string? DisplayName,
    string? Authority,
    string? ClientId,
    string? ClientSecret,
    string Scopes,
    string RoleClaimName,
    Users.UserRole DefaultRole,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>The all-false/all-null shape <see cref="IOidcSettingsStore.GetAsync"/>
    /// returns when no row exists yet - generic OpenID Connect has never been configured
    /// on this instance. Matches the migration's own column defaults.</summary>
    public static readonly OidcSettings NotConfigured = new(
        Enabled: false,
        DisplayName: null,
        Authority: null,
        ClientId: null,
        ClientSecret: null,
        Scopes: "openid profile email",
        RoleClaimName: "roles",
        DefaultRole: Users.UserRole.Viewer,
        UpdatedAt: null);
}
