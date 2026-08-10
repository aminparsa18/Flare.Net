using Flare.Identity.Users;

namespace Flare.Identity.Auth;

/// <summary>Microsoft Entra ID (SSO) configuration, bound from the <c>Auth:Entra</c>
/// section. See docs/auth.md's "Microsoft Entra ID (SSO)" section for the App
/// Registration setup these values come from.</summary>
public sealed class EntraOptions
{
    public const string SectionName = "Auth:Entra";

    /// <summary>Off by default - upgrading an existing deployment must not suddenly
    /// require Entra config to be present, same reasoning as <see cref="AuthOptions"/>'s
    /// sibling <c>Auth:IngestKeyRequired</c> default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Entra Directory (tenant) ID. Single-tenant only for v1 - see
    /// docs/auth.md for why "any Entra org can sign in" is the wrong default for a
    /// self-hosted internal tool.</summary>
    public string? TenantId { get; set; }

    /// <summary>The App Registration's Application (client) ID.</summary>
    public string? ClientId { get; set; }

    /// <summary>The App Registration's client secret. No working default - same
    /// "blank until configured" convention <c>EmailOptions</c> already uses for SMTP
    /// credentials.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Role assigned to a newly-provisioned Entra user when their ID token
    /// carries no recognized <c>roles</c> claim entry (no App Role assigned in Entra, or
    /// App Roles not configured at all) - least-privilege default.</summary>
    public UserRole DefaultRole { get; set; } = UserRole.Viewer;
}
