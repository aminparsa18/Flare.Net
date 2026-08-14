namespace Flare.Identity.Auth;

public interface IOidcSettingsStore
{
    /// <summary>Never null - returns <see cref="OidcSettings.NotConfigured"/> when no row
    /// exists yet.</summary>
    Task<OidcSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary><paramref name="clientSecret"/> of <c>null</c> leaves the currently-stored
    /// secret unchanged (the dashboard's "blank means unchanged" field) - same convention
    /// as <see cref="IEntraSettingsStore.SaveAsync"/>'s client secret and
    /// <see cref="ILdapSettingsStore.SaveAsync"/>'s bind password.</summary>
    Task<OidcSettings> SaveAsync(
        bool enabled,
        string? displayName,
        string? authority,
        string? clientId,
        string? clientSecret,
        string scopes,
        string roleClaimName,
        Users.UserRole defaultRole,
        CancellationToken cancellationToken = default);
}
