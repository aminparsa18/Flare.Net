namespace Flare.Identity.Auth;

public interface IProxyAuthSettingsStore
{
    /// <summary>Never null - returns <see cref="ProxyAuthSettings.NotConfigured"/> when
    /// no row exists yet.</summary>
    Task<ProxyAuthSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>No secret field here (unlike <see cref="IEntraSettingsStore.SaveAsync"/>/
    /// <see cref="ILdapSettingsStore.SaveAsync"/>/<see cref="IOidcSettingsStore.SaveAsync"/>)
    /// - every field is always supplied together, plain overwrite.</summary>
    Task<ProxyAuthSettings> SaveAsync(
        bool enabled,
        string headerName,
        string trustedProxyCidrs,
        string? groupsHeaderName,
        string? adminGroup,
        string? memberGroup,
        string? viewerGroup,
        Users.UserRole defaultRole,
        string? logoutRedirectUrl,
        CancellationToken cancellationToken = default);
}
