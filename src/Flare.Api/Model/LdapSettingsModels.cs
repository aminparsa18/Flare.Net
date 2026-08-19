using Flare.Identity.Users;

namespace Flare.Api.Model;

/// <summary>Response body for <c>GET /api/settings/ldap</c> - the consolidated
/// <c>/auth</c> screen's Active Directory section. Never carries the real bind
/// password - see <see cref="HasBindPassword"/>.</summary>
public sealed record LdapSettingsDto
{
    public required bool Enabled { get; init; }

    public string? Host { get; init; }

    public required int Port { get; init; }

    public required bool UseSsl { get; init; }

    public string? BaseDn { get; init; }

    public string? BindDn { get; init; }

    /// <summary>True once a bind password has been saved at least once - mirrors
    /// <see cref="EntraSettingsDto.HasClientSecret"/>'s "will not be displayed once
    /// set" convention.</summary>
    public required bool HasBindPassword { get; init; }

    public required string UserSearchFilter { get; init; }

    public required string UniqueIdAttribute { get; init; }

    public string? AdminGroupDn { get; init; }

    public string? MemberGroupDn { get; init; }

    public string? ViewerGroupDn { get; init; }

    public required UserRole DefaultRole { get; init; }

    /// <summary>PEM-encoded certificate pinned as the sole TLS trust anchor for LDAP
    /// connections - unlike <see cref="HasBindPassword"/>, this is echoed back in full
    /// (not redacted behind a boolean): a certificate isn't a secret. Null means no pin is
    /// configured (falls back to the OS/container trust store).</summary>
    public string? PinnedCertificatePem { get; init; }
}

/// <summary>Request body for <c>PUT /api/settings/ldap</c>. <see cref="BindPassword"/>
/// of <c>null</c>/omitted leaves the currently-stored password unchanged - see
/// <c>ILdapSettingsStore.SaveAsync</c>'s remarks. <see cref="PinnedCertificatePem"/> does
/// NOT follow that convention: <c>null</c>/omitted always *clears* any previously-saved
/// pin.</summary>
public sealed record SaveLdapSettingsRequest
{
    public required bool Enabled { get; init; }

    public string? Host { get; init; }

    public required int Port { get; init; }

    public required bool UseSsl { get; init; }

    public string? BaseDn { get; init; }

    public string? BindDn { get; init; }

    public string? BindPassword { get; init; }

    public required string UserSearchFilter { get; init; }

    public required string UniqueIdAttribute { get; init; }

    public string? AdminGroupDn { get; init; }

    public string? MemberGroupDn { get; init; }

    public string? ViewerGroupDn { get; init; }

    public required UserRole DefaultRole { get; init; }

    public string? PinnedCertificatePem { get; init; }
}
