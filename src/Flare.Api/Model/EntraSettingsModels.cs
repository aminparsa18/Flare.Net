using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Response body for <c>GET</c>/<c>PUT /api/settings/entra</c> - the Admin-only
/// Security screen's shape. Never carries the real client secret - see
/// <see cref="HasClientSecret"/>.</summary>
[MemoryPackable]
public sealed partial record EntraSettingsDto
{
    public required bool Enabled { get; init; }

    public string? TenantId { get; init; }

    public string? ClientId { get; init; }

    /// <summary>True once a client secret has been saved at least once - mirrors Seq's
    /// own "will not be displayed once set" client-secret field. The dashboard shows a
    /// masked placeholder when this is true and leaves the secret input blank
    /// (unchanged) unless the Admin types a new one.</summary>
    public required bool HasClientSecret { get; init; }

    /// <summary>Computed from the current request's scheme+host - the exact value to
    /// register as this App Registration's redirect URI in the Entra portal.</summary>
    public required string RedirectUri { get; init; }
}

/// <summary>Request body for <c>PUT /api/settings/entra</c>. <see cref="ClientSecret"/>
/// of <c>null</c>/omitted leaves the currently-stored secret unchanged - see
/// <c>IEntraSettingsStore.SaveAsync</c>'s remarks.</summary>
[MemoryPackable]
public sealed partial record SaveEntraSettingsRequest
{
    public required bool Enabled { get; init; }

    public string? TenantId { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }
}
