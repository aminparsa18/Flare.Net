using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class SqliteProxyAuthSettingsStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteProxyAuthSettingsStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteProxyAuthSettingsStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAsync_ReturnsNotConfigured_WhenNoRowExistsYet()
    {
        var settings = await _store.GetAsync();

        Assert.Equal(ProxyAuthSettings.NotConfigured, settings);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        await _store.SaveAsync(
            enabled: true,
            headerName: "Remote-User",
            trustedProxyCidrs: "172.18.0.0/16",
            groupsHeaderName: "X-Forwarded-Groups",
            adminGroup: "admins",
            memberGroup: "members",
            viewerGroup: "viewers",
            defaultRole: UserRole.Member);

        var settings = await _store.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal("Remote-User", settings.HeaderName);
        Assert.Equal("172.18.0.0/16", settings.TrustedProxyCidrs);
        Assert.Equal("X-Forwarded-Groups", settings.GroupsHeaderName);
        Assert.Equal("admins", settings.AdminGroup);
        Assert.Equal("members", settings.MemberGroup);
        Assert.Equal("viewers", settings.ViewerGroup);
        Assert.Equal(UserRole.Member, settings.DefaultRole);
        Assert.NotNull(settings.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_OverwritesEveryField()
    {
        await _store.SaveAsync(true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer);

        var updated = await _store.SaveAsync(true, "X-Auth-User", "10.0.0.0/8", "X-Groups", "a", "m", "v", UserRole.Admin);

        Assert.Equal("X-Auth-User", updated.HeaderName);
        Assert.Equal("10.0.0.0/8", updated.TrustedProxyCidrs);
        Assert.Equal("X-Groups", updated.GroupsHeaderName);
        Assert.Equal(UserRole.Admin, updated.DefaultRole);
    }

    [Fact]
    public async Task SaveAsync_CanDisableWithoutClearingOtherFields()
    {
        await _store.SaveAsync(true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer);

        var disabled = await _store.SaveAsync(false, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer);

        Assert.False(disabled.Enabled);
        Assert.Equal("172.18.0.0/16", disabled.TrustedProxyCidrs);
    }

    [Fact]
    public async Task SaveAsync_PersistsNullOptionalGroupFields()
    {
        var saved = await _store.SaveAsync(true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer);

        Assert.Null(saved.GroupsHeaderName);
        Assert.Null(saved.AdminGroup);
        Assert.Null(saved.MemberGroup);
        Assert.Null(saved.ViewerGroup);
    }
}
