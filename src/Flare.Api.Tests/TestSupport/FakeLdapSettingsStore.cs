using Flare.Identity.Auth;
using Flare.Identity.Users;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="ILdapSettingsStore"/> - same convention as
/// <see cref="FakeEntraSettingsStore"/>.</summary>
internal sealed class FakeLdapSettingsStore : ILdapSettingsStore
{
    private LdapSettings _settings = LdapSettings.NotConfigured;

    public FakeLdapSettingsStore()
    {
    }

    public FakeLdapSettingsStore(LdapSettings initial)
    {
        _settings = initial;
    }

    public Task<LdapSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task<LdapSettings> SaveAsync(
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
        UserRole defaultRole,
        CancellationToken cancellationToken = default)
    {
        _settings = _settings with
        {
            Enabled = enabled,
            Host = host,
            Port = port,
            UseSsl = useSsl,
            BaseDn = baseDn,
            BindDn = bindDn,
            BindPassword = bindPassword ?? _settings.BindPassword,
            UserSearchFilter = userSearchFilter,
            UniqueIdAttribute = uniqueIdAttribute,
            AdminGroupDn = adminGroupDn,
            MemberGroupDn = memberGroupDn,
            ViewerGroupDn = viewerGroupDn,
            DefaultRole = defaultRole,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return Task.FromResult(_settings);
    }
}
