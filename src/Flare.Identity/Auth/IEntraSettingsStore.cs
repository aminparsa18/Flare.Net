namespace Flare.Identity.Auth;

public interface IEntraSettingsStore
{
    /// <summary>Never null - returns <see cref="EntraSettings.NotConfigured"/> when no
    /// row exists yet.</summary>
    Task<EntraSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary><paramref name="clientSecret"/> of <c>null</c> leaves the currently-stored
    /// secret unchanged (the dashboard's "blank means unchanged" field) - pass an empty
    /// string, not null, if a caller ever needs to actually clear it (no current caller
    /// does; see <c>EntraSettingsEndpoints</c>'s remarks on why clearing isn't exposed).</summary>
    Task<EntraSettings> SaveAsync(bool enabled, string? tenantId, string? clientId, string? clientSecret, CancellationToken cancellationToken = default);
}
