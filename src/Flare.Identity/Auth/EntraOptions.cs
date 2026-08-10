using Flare.Identity.Users;

namespace Flare.Identity.Auth;

/// <summary>Microsoft Entra ID (SSO) config-bound settings, bound from the
/// <c>Auth:Entra</c> section - the one Entra-related value that stays config-driven
/// rather than living in <see cref="EntraSettings"/>/<see cref="IEntraSettingsStore"/>
/// (the dashboard's Security screen doesn't configure a role-mapping fallback either).
/// <c>Enabled</c>/<c>TenantId</c>/<c>ClientId</c>/<c>ClientSecret</c> used to live here
/// too - see docs/auth.md for why those moved to the database-backed Security screen.</summary>
public sealed class EntraOptions
{
    public const string SectionName = "Auth:Entra";

    /// <summary>Role assigned to a newly-provisioned Entra user when their ID token
    /// carries no recognized <c>roles</c> claim entry (no App Role assigned in Entra, or
    /// App Roles not configured at all) - least-privilege default.</summary>
    public UserRole DefaultRole { get; set; } = UserRole.Viewer;
}
