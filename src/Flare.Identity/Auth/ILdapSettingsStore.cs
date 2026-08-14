namespace Flare.Identity.Auth;

public interface ILdapSettingsStore
{
    /// <summary>Never null - returns <see cref="LdapSettings.NotConfigured"/> when no
    /// row exists yet.</summary>
    Task<LdapSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary><paramref name="bindPassword"/> of <c>null</c> leaves the currently-stored
    /// password unchanged (the dashboard's "blank means unchanged" field) - same
    /// convention as <see cref="IEntraSettingsStore.SaveAsync"/>'s client secret.</summary>
    Task<LdapSettings> SaveAsync(
        bool enabled,
        string? host,
        int port,
        bool useSsl,
        string? baseDn,
        string? bindDn,
        string? bindPassword,
        string userSearchFilter,
        string uniqueIdAttribute,
        string? adminGroupDn,
        string? memberGroupDn,
        string? viewerGroupDn,
        Users.UserRole defaultRole,
        CancellationToken cancellationToken = default);
}
