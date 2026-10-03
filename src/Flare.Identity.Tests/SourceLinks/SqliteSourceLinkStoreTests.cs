using Flare.Identity.SourceLinks;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.SourceLinks;

public class SqliteSourceLinkStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteSourceLinkStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteSourceLinkStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoRows_ReturnsEmpty() => Assert.Empty(await _store.GetAllAsync());

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_RoundTripsEveryField()
    {
        var config = new SourceLinkConfig("checkout-api", SourceLinkProvider.GitLab, "https://gitlab.com/acme/shop", "main", "/src/");

        await _store.SetAsync(config);

        Assert.Equal(config, Assert.Single(await _store.GetAllAsync()));
    }

    [Fact]
    public async Task SetAsync_CalledTwiceForTheSameService_Upserts()
    {
        await _store.SetAsync(new SourceLinkConfig("checkout-api", SourceLinkProvider.GitHub, "https://github.com/a/b", "main", ""));
        await _store.SetAsync(new SourceLinkConfig("checkout-api", SourceLinkProvider.AzureDevOps, "https://dev.azure.com/o/p/_git/r", "dev", "/_/"));

        var stored = Assert.Single(await _store.GetAllAsync());
        Assert.Equal(SourceLinkProvider.AzureDevOps, stored.Provider);
        Assert.Equal("dev", stored.DefaultRef);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatService()
    {
        await _store.SetAsync(new SourceLinkConfig("a", SourceLinkProvider.GitHub, "https://github.com/x/a", "", ""));
        await _store.SetAsync(new SourceLinkConfig("b", SourceLinkProvider.GitHub, "https://github.com/x/b", "", ""));

        await _store.DeleteAsync("a");
        await _store.DeleteAsync("missing");

        Assert.Equal("b", Assert.Single(await _store.GetAllAsync()).ServiceName);
    }
}
