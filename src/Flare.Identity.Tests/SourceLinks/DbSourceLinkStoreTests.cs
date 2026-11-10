using Flare.Identity.SourceLinks;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.SourceLinks;

public class DbSourceLinkStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbSourceLinkStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbSourceLinkStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoRows_ReturnsEmpty() => Assert.Empty(await _store.GetAllAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_RoundTripsEveryField()
    {
        var config = new SourceLinkConfig("checkout-api", SourceLinkProvider.GitLab, "https://gitlab.com/acme/shop", "main", "/src/");

        await _store.SetAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(config, Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task SetAsync_CalledTwiceForTheSameService_Upserts()
    {
        await _store.SetAsync(new SourceLinkConfig("checkout-api", SourceLinkProvider.GitHub, "https://github.com/a/b", "main", ""), TestContext.Current.CancellationToken);
        await _store.SetAsync(new SourceLinkConfig("checkout-api", SourceLinkProvider.AzureDevOps, "https://dev.azure.com/o/p/_git/r", "dev", "/_/"), TestContext.Current.CancellationToken);

        var stored = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(SourceLinkProvider.AzureDevOps, stored.Provider);
        Assert.Equal("dev", stored.DefaultRef);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatService()
    {
        await _store.SetAsync(new SourceLinkConfig("a", SourceLinkProvider.GitHub, "https://github.com/x/a", "", ""), TestContext.Current.CancellationToken);
        await _store.SetAsync(new SourceLinkConfig("b", SourceLinkProvider.GitHub, "https://github.com/x/b", "", ""), TestContext.Current.CancellationToken);

        await _store.DeleteAsync("a", TestContext.Current.CancellationToken);
        await _store.DeleteAsync("missing", TestContext.Current.CancellationToken);

        Assert.Equal("b", Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).ServiceName);
    }

    [Fact]
    public async Task SetAsync_NullTokenKeepsStoredToken_EmptyClearsIt()
    {
        SourceLinkConfig With(string? token) => new("a", SourceLinkProvider.GitHub, "https://github.com/x/a", "", "", token);

        await _store.SetAsync(With("secret"), TestContext.Current.CancellationToken);
        await _store.SetAsync(With(null), TestContext.Current.CancellationToken);
        Assert.Equal("secret", Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).AccessToken);

        await _store.SetAsync(With(""), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).AccessToken);
    }

    [Fact]
    public async Task SetAsync_IssueTokenIsIndependentOfAccessToken_NullKeepsEmptyClears()
    {
        SourceLinkConfig With(string? access, string? issue) => new("a", SourceLinkProvider.GitHub, "https://github.com/x/a", "", "", access, issue);

        await _store.SetAsync(With("read", "write"), TestContext.Current.CancellationToken);
        await _store.SetAsync(With(null, null), TestContext.Current.CancellationToken);
        var kept = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("read", kept.AccessToken);
        Assert.Equal("write", kept.IssueToken);

        await _store.SetAsync(With(null, ""), TestContext.Current.CancellationToken);
        var cleared = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("read", cleared.AccessToken);
        Assert.Null(cleared.IssueToken);
    }
}
