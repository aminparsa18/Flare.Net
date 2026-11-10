using Flare.Identity.SourceMaps;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.SourceMaps;

public class DbSourceMapStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbSourceMapStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbSourceMapStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTripsContent()
    {
        await _store.UpsertAsync("web", "1.0.0", "assets/app.js", [1, 2, 3], 100, TestContext.Current.CancellationToken);

        var stored = await _store.GetAsync("web", "1.0.0", "assets/app.js", TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3 }, stored!.Value.GzipContent);
        Assert.Null(await _store.GetAsync("web", "1.0.1", "assets/app.js", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_ForTheSameBundle_Replaces()
    {
        await _store.UpsertAsync("web", "1.0.0", "assets/app.js", [1], 10, TestContext.Current.CancellationToken);
        await _store.UpsertAsync("web", "1.0.0", "assets/app.js", [9, 9], 20, TestContext.Current.CancellationToken);

        var info = Assert.Single(await _store.ListAsync("web", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Equal(20, info.SizeBytes);
        Assert.Equal(new byte[] { 9, 9 }, (await _store.GetAsync("web", "1.0.0", "assets/app.js", TestContext.Current.CancellationToken))!.Value.GzipContent);
    }

    [Fact]
    public async Task ListAsync_FiltersByServiceAndVersion()
    {
        await _store.UpsertAsync("web", "1", "a.js", [1], 1, TestContext.Current.CancellationToken);
        await _store.UpsertAsync("web", "2", "a.js", [1], 1, TestContext.Current.CancellationToken);
        await _store.UpsertAsync("admin", "1", "a.js", [1], 1, TestContext.Current.CancellationToken);

        Assert.Equal(3, (await _store.ListAsync(null, null, TestContext.Current.CancellationToken)).Count);
        Assert.Equal(2, (await _store.ListAsync("web", null, TestContext.Current.CancellationToken)).Count);
        Assert.Single(await _store.ListAsync("web", "2", TestContext.Current.CancellationToken));
        Assert.Equal(["a.js"], await _store.ListBundlesAsync("admin", "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOneBundleOrTheWholeVersion()
    {
        await _store.UpsertAsync("web", "1", "a.js", [1], 1, TestContext.Current.CancellationToken);
        await _store.UpsertAsync("web", "1", "b.js", [1], 1, TestContext.Current.CancellationToken);

        Assert.Equal(1, await _store.DeleteAsync("web", "1", "a.js", TestContext.Current.CancellationToken));
        Assert.Equal(1, await _store.DeleteAsync("web", "1", null, TestContext.Current.CancellationToken));
        Assert.Empty(await _store.ListAsync(null, null, TestContext.Current.CancellationToken));
    }
}
