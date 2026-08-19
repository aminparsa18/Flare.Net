namespace Flare.Identity.Auth;

/// <summary>
/// Active Directory (LDAP) settings as configured through the dashboard's Admin-only
/// <c>/auth</c> screen - see docs/auth.md's "Active Directory (LDAP)" section.
/// Deliberately carries the real <see cref="BindPassword"/> - like
/// <see cref="EntraSettings.ClientSecret"/>, this has to be reversible (Flare binds to
/// the LDAP server with it on every login attempt), so it never appears in an API
/// response; see <c>LdapSettingsEndpoints</c>'s <c>hasBindPassword</c> boolean instead.
/// </summary>
/// <param name="PinnedCertificatePem">PEM-encoded certificate pinned as the sole TLS
/// trust anchor for LDAP connections - either an internal CA's root certificate, or the
/// domain controller's own certificate if self-signed. Unlike <see cref="BindPassword"/>,
/// this is plaintext AND echoed back in full by <c>LdapSettingsEndpoints</c> - a
/// certificate isn't a secret, so there's no <c>hasX</c> boolean indirection for it.
/// Null/empty means "no pin configured" - <c>LdapAuthEndpoints</c>'s connection factory
/// falls back to the OS/container trust store, today's unchanged default behavior.</param>
public sealed record LdapSettings(
    bool Enabled,
    string? Host,
    int Port,
    bool UseSsl,
    string? BaseDn,
    string? BindDn,
    string? BindPassword,
    string UserSearchFilter,
    string UniqueIdAttribute,
    string? AdminGroupDn,
    string? MemberGroupDn,
    string? ViewerGroupDn,
    Users.UserRole DefaultRole,
    DateTimeOffset? UpdatedAt,
    string? PinnedCertificatePem = null)
{
    /// <summary>The shape <see cref="ILdapSettingsStore.GetAsync"/> returns when no row
    /// exists yet - Active Directory has never been configured on this instance. Matches
    /// the migration's own column defaults.</summary>
    public static readonly LdapSettings NotConfigured = new(
        Enabled: false,
        Host: null,
        Port: 636,
        UseSsl: true,
        BaseDn: null,
        BindDn: null,
        BindPassword: null,
        UserSearchFilter: "(&(objectClass=user)(sAMAccountName={0}))",
        UniqueIdAttribute: "objectGUID",
        AdminGroupDn: null,
        MemberGroupDn: null,
        ViewerGroupDn: null,
        DefaultRole: Users.UserRole.Viewer,
        UpdatedAt: null,
        PinnedCertificatePem: null);
}
