using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class SqliteEntraSettingsStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteEntraSettingsStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteEntraSettingsStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAsync_ReturnsNotConfigured_WhenNoRowExistsYet()
    {
        var settings = await _store.GetAsync();

        Assert.Equal(EntraSettings.NotConfigured, settings);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        await _store.SaveAsync(enabled: true, tenantId: "tenant-1", clientId: "client-1", clientSecret: "secret-1");

        var settings = await _store.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal("tenant-1", settings.TenantId);
        Assert.Equal("client-1", settings.ClientId);
        Assert.Equal("secret-1", settings.ClientSecret);
        Assert.NotNull(settings.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_WithNullClientSecret_PreservesTheExistingSecret()
    {
        await _store.SaveAsync(enabled: true, tenantId: "tenant-1", clientId: "client-1", clientSecret: "original-secret");

        // Admin edits TenantId only, leaving the client secret field blank in the UI -
        // the dashboard sends clientSecret: null for "unchanged".
        var updated = await _store.SaveAsync(enabled: true, tenantId: "tenant-2", clientId: "client-1", clientSecret: null);

        Assert.Equal("tenant-2", updated.TenantId);
        Assert.Equal("original-secret", updated.ClientSecret);

        var fetched = await _store.GetAsync();
        Assert.Equal("original-secret", fetched.ClientSecret);
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceWithARealSecret_OverwritesIt()
    {
        await _store.SaveAsync(enabled: true, tenantId: "tenant-1", clientId: "client-1", clientSecret: "old-secret");
        await _store.SaveAsync(enabled: true, tenantId: "tenant-1", clientId: "client-1", clientSecret: "new-secret");

        var settings = await _store.GetAsync();

        Assert.Equal("new-secret", settings.ClientSecret);
    }

    [Fact]
    public async Task SaveAsync_CanDisableWithoutTouchingStoredCredentials()
    {
        await _store.SaveAsync(enabled: true, tenantId: "tenant-1", clientId: "client-1", clientSecret: "secret-1");

        var disabled = await _store.SaveAsync(enabled: false, tenantId: "tenant-1", clientId: "client-1", clientSecret: null);

        Assert.False(disabled.Enabled);
        Assert.Equal("secret-1", disabled.ClientSecret);
    }
}
