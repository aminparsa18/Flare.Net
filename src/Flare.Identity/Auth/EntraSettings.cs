namespace Flare.Identity.Auth;

/// <summary>
/// Microsoft Entra ID (SSO) settings as configured through the dashboard's Admin-only
/// Security screen - see docs/auth.md's "Microsoft Entra ID (SSO)" section. Deliberately
/// carries the real <see cref="ClientSecret"/> - unlike <see cref="Users.User"/>'s
/// password hash, this has to be reversible (Flare sends it to Microsoft on every token
/// exchange), so it never appears in an API response; see
/// <c>EntraSettingsEndpoints</c>'s <c>hasClientSecret</c> boolean instead.
/// </summary>
public sealed record EntraSettings(bool Enabled, string? TenantId, string? ClientId, string? ClientSecret, DateTimeOffset? UpdatedAt)
{
    /// <summary>The all-false/all-null shape <see cref="IEntraSettingsStore.GetAsync"/>
    /// returns when no row exists yet - Entra has never been configured on this
    /// instance.</summary>
    public static readonly EntraSettings NotConfigured = new(Enabled: false, TenantId: null, ClientId: null, ClientSecret: null, UpdatedAt: null);
}
