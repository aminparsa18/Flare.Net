using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class SqliteLdapSettingsStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteLdapSettingsStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteLdapSettingsStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAsync_ReturnsNotConfigured_WhenNoRowExistsYet()
    {
        var settings = await _store.GetAsync();

        Assert.Equal(LdapSettings.NotConfigured, settings);
    }

    // Not a real certificate - the store treats PinnedCertificatePem as an opaque string
    // (PEM-format validation happens at LdapSettingsEndpoints.HandlePutAsync, not here),
    // so any placeholder value is enough to exercise persistence/round-tripping.
    private const string SamplePinnedCertificatePem = "-----BEGIN CERTIFICATE-----\nplaceholder\n-----END CERTIFICATE-----";

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        await _store.SaveAsync(
            enabled: true,
            host: "dc.corp.example.com",
            port: 636,
            useSsl: true,
            pinnedCertificatePem: SamplePinnedCertificatePem,
            baseDn: "DC=corp,DC=example,DC=com",
            bindDn: "CN=flare-svc,OU=Service Accounts,DC=corp,DC=example,DC=com",
            bindPassword: "service-account-secret",
            userSearchFilter: "(&(objectClass=user)(sAMAccountName={0}))",
            uniqueIdAttribute: "objectGUID",
            adminGroupDn: "CN=Flare Admins,DC=corp,DC=example,DC=com",
            memberGroupDn: "CN=Flare Members,DC=corp,DC=example,DC=com",
            viewerGroupDn: "CN=Flare Viewers,DC=corp,DC=example,DC=com",
            defaultRole: UserRole.Viewer);

        var settings = await _store.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal("dc.corp.example.com", settings.Host);
        Assert.Equal(636, settings.Port);
        Assert.True(settings.UseSsl);
        Assert.Equal("DC=corp,DC=example,DC=com", settings.BaseDn);
        Assert.Equal("service-account-secret", settings.BindPassword);
        Assert.Equal("CN=Flare Admins,DC=corp,DC=example,DC=com", settings.AdminGroupDn);
        Assert.Equal(UserRole.Viewer, settings.DefaultRole);
        Assert.Equal(SamplePinnedCertificatePem, settings.PinnedCertificatePem);
        Assert.NotNull(settings.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_WithNullBindPassword_PreservesTheExistingPassword()
    {
        await SaveWithPassword("original-secret");

        var updated = await SaveWithPassword(null, host: "dc2.corp.example.com");

        Assert.Equal("dc2.corp.example.com", updated.Host);
        Assert.Equal("original-secret", updated.BindPassword);
    }

    [Fact]
    public async Task SaveAsync_UsesDefaultsForFilterAndUniqueIdAttribute_WhenCallerSuppliesThem()
    {
        var saved = await SaveWithPassword("secret-1");

        Assert.Equal("(&(objectClass=user)(sAMAccountName={0}))", saved.UserSearchFilter);
        Assert.Equal("objectGUID", saved.UniqueIdAttribute);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsThePinnedCertificate()
    {
        var saved = await SaveWithPassword("secret-1", pinnedCertificatePem: SamplePinnedCertificatePem);

        Assert.Equal(SamplePinnedCertificatePem, saved.PinnedCertificatePem);
        Assert.Equal(SamplePinnedCertificatePem, (await _store.GetAsync()).PinnedCertificatePem);
    }

    [Fact]
    public async Task SaveAsync_WithNullPinnedCertificatePem_ClearsAnyPreviouslySavedPin()
    {
        await SaveWithPassword("secret-1", pinnedCertificatePem: SamplePinnedCertificatePem);

        // Deliberate contrast with SaveAsync_WithNullBindPassword_PreservesTheExistingPassword
        // above: unlike BindPassword, PinnedCertificatePem isn't a secret, so a null here
        // is a real clear, not "leave unchanged" - see ILdapSettingsStore.SaveAsync's remarks.
        var updated = await SaveWithPassword("secret-1", pinnedCertificatePem: null);

        Assert.Null(updated.PinnedCertificatePem);
    }

    private async Task<LdapSettings> SaveWithPassword(string? bindPassword, string host = "dc.corp.example.com", string? pinnedCertificatePem = null) =>
        await _store.SaveAsync(
            enabled: true,
            host: host,
            port: 636,
            useSsl: true,
            pinnedCertificatePem: pinnedCertificatePem,
            baseDn: "DC=corp,DC=example,DC=com",
            bindDn: "CN=flare-svc,DC=corp,DC=example,DC=com",
            bindPassword: bindPassword,
            userSearchFilter: "(&(objectClass=user)(sAMAccountName={0}))",
            uniqueIdAttribute: "objectGUID",
            adminGroupDn: null,
            memberGroupDn: null,
            viewerGroupDn: null,
            defaultRole: UserRole.Viewer);
}
