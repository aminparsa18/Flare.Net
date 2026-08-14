namespace Flare.Identity.Auth;

/// <summary>
/// Reverse-proxy / trusted-header settings as configured through the dashboard's
/// Admin-only <c>/auth</c> screen - see docs/auth.md's "Reverse proxy (trusted header)"
/// section. Trusts an identity header an already-authenticating reverse proxy in front
/// of Flare (Authelia, Authentik, oauth2-proxy, Cloudflare Access, Tailscale Serve, ...)
/// injects on every request, instead of Flare talking to an IdP itself. Unlike
/// <see cref="EntraSettings"/>/<see cref="LdapSettings"/>/<see cref="OidcSettings"/>,
/// this record carries no secret at all - there's nothing to mask, so it never needs
/// the "blank means unchanged" convention the other three settings stores use for
/// <c>SaveAsync</c>.
/// </summary>
public sealed record ProxyAuthSettings(
    bool Enabled,
    string HeaderName,
    /// <summary>One or more CIDR entries (newline/comma separated), parsed via
    /// <see cref="TrustedProxyNetworks"/> - see that class's remarks for why this is
    /// mandatory, not optional, to actually enable this method.</summary>
    string TrustedProxyCidrs,
    string? GroupsHeaderName,
    string? AdminGroup,
    string? MemberGroup,
    string? ViewerGroup,
    Users.UserRole DefaultRole,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>The shape <see cref="IProxyAuthSettingsStore.GetAsync"/> returns when no
    /// row exists yet - reverse-proxy auth has never been configured on this instance.
    /// Matches the migration's own column defaults.</summary>
    public static readonly ProxyAuthSettings NotConfigured = new(
        Enabled: false,
        HeaderName: "Remote-User",
        TrustedProxyCidrs: "",
        GroupsHeaderName: null,
        AdminGroup: null,
        MemberGroup: null,
        ViewerGroup: null,
        DefaultRole: Users.UserRole.Viewer,
        UpdatedAt: null);
}
