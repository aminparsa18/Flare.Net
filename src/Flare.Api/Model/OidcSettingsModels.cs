using Flare.Identity.Users;
using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Response body for <c>GET</c>/<c>PUT /api/settings/oidc</c> - the Admin-only
/// Security screen's generic OpenID Connect section. Never carries the real client
/// secret - see <see cref="HasClientSecret"/>.</summary>
[MemoryPackable]
public sealed partial record OidcSettingsDto
{
    public required bool Enabled { get; init; }

    /// <summary>Drives the <c>/login</c> page's "Sign in with {DisplayName}" button
    /// label - a generic provider has no fixed brand the way Entra's "Microsoft" does.</summary>
    public string? DisplayName { get; init; }

    public string? Authority { get; init; }

    public string? ClientId { get; init; }

    /// <summary>True once a client secret has been saved at least once - same "will not
    /// be displayed once set" convention as <see cref="EntraSettingsDto.HasClientSecret"/>.</summary>
    public required bool HasClientSecret { get; init; }

    public required string Scopes { get; init; }

    public required string RoleClaimName { get; init; }

    public required UserRole DefaultRole { get; init; }

    /// <summary>Computed from the current request's scheme+host plus
    /// <see cref="Auth.OidcAuthenticationDefaults.CallbackPath"/> - the exact value to
    /// register as this provider's callback/redirect URI.</summary>
    public required string RedirectUri { get; init; }
}

/// <summary>Request body for <c>PUT /api/settings/oidc</c>. <see cref="ClientSecret"/> of
/// <c>null</c>/omitted leaves the currently-stored secret unchanged - see
/// <c>IOidcSettingsStore.SaveAsync</c>'s remarks.</summary>
[MemoryPackable]
public sealed partial record SaveOidcSettingsRequest
{
    public required bool Enabled { get; init; }

    public string? DisplayName { get; init; }

    public string? Authority { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public required string Scopes { get; init; }

    public required string RoleClaimName { get; init; }

    public required UserRole DefaultRole { get; init; }
}
