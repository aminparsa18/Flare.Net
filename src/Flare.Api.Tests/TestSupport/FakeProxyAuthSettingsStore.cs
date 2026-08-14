using Flare.Identity.Auth;
using Flare.Identity.Users;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IProxyAuthSettingsStore"/> - same convention as
/// <see cref="FakeEntraSettingsStore"/>/<see cref="FakeLdapSettingsStore"/>/
/// <see cref="FakeOidcSettingsStore"/>.</summary>
internal sealed class FakeProxyAuthSettingsStore : IProxyAuthSettingsStore
{
    private ProxyAuthSettings _settings = ProxyAuthSettings.NotConfigured;

    public FakeProxyAuthSettingsStore()
    {
    }

    public FakeProxyAuthSettingsStore(ProxyAuthSettings initial)
    {
        _settings = initial;
    }

    public Task<ProxyAuthSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task<ProxyAuthSettings> SaveAsync(
        bool enabled,
        string headerName,
        string trustedProxyCidrs,
        string? groupsHeaderName,
        string? adminGroup,
        string? memberGroup,
        string? viewerGroup,
        UserRole defaultRole,
        CancellationToken cancellationToken = default)
    {
        _settings = _settings with
        {
            Enabled = enabled,
            HeaderName = headerName,
            TrustedProxyCidrs = trustedProxyCidrs,
            GroupsHeaderName = groupsHeaderName,
            AdminGroup = adminGroup,
            MemberGroup = memberGroup,
            ViewerGroup = viewerGroup,
            DefaultRole = defaultRole,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return Task.FromResult(_settings);
    }
}
