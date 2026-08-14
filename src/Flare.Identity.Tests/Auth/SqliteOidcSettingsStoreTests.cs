using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class SqliteOidcSettingsStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteOidcSettingsStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteOidcSettingsStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAsync_ReturnsNotConfigured_WhenNoRowExistsYet()
    {
        var settings = await _store.GetAsync();

        Assert.Equal(OidcSettings.NotConfigured, settings);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        await _store.SaveAsync(
            enabled: true,
            displayName: "Okta",
            authority: "https://example.okta.com",
            clientId: "client-1",
            clientSecret: "secret-1",
            scopes: "openid profile email",
            roleClaimName: "roles",
            defaultRole: UserRole.Member);

        var settings = await _store.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal("Okta", settings.DisplayName);
        Assert.Equal("https://example.okta.com", settings.Authority);
        Assert.Equal("client-1", settings.ClientId);
        Assert.Equal("secret-1", settings.ClientSecret);
        Assert.Equal("openid profile email", settings.Scopes);
        Assert.Equal("roles", settings.RoleClaimName);
        Assert.Equal(UserRole.Member, settings.DefaultRole);
        Assert.NotNull(settings.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_WithNullClientSecret_PreservesTheExistingSecret()
    {
        await _store.SaveAsync(true, "Okta", "https://example.okta.com", "client-1", "original-secret", "openid profile email", "roles", UserRole.Viewer);

        // Admin edits the Authority only, leaving the client secret field blank in the UI -
        // the dashboard sends clientSecret: null for "unchanged".
        var updated = await _store.SaveAsync(true, "Okta", "https://example2.okta.com", "client-1", null, "openid profile email", "roles", UserRole.Viewer);

        Assert.Equal("https://example2.okta.com", updated.Authority);
        Assert.Equal("original-secret", updated.ClientSecret);

        var fetched = await _store.GetAsync();
        Assert.Equal("original-secret", fetched.ClientSecret);
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceWithARealSecret_OverwritesIt()
    {
        await _store.SaveAsync(true, "Okta", "https://example.okta.com", "client-1", "old-secret", "openid profile email", "roles", UserRole.Viewer);
        await _store.SaveAsync(true, "Okta", "https://example.okta.com", "client-1", "new-secret", "openid profile email", "roles", UserRole.Viewer);

        var settings = await _store.GetAsync();

        Assert.Equal("new-secret", settings.ClientSecret);
    }

    [Fact]
    public async Task SaveAsync_CanDisableWithoutTouchingStoredCredentials()
    {
        await _store.SaveAsync(true, "Okta", "https://example.okta.com", "client-1", "secret-1", "openid profile email", "roles", UserRole.Viewer);

        var disabled = await _store.SaveAsync(false, "Okta", "https://example.okta.com", "client-1", null, "openid profile email", "roles", UserRole.Viewer);

        Assert.False(disabled.Enabled);
        Assert.Equal("secret-1", disabled.ClientSecret);
    }
}
