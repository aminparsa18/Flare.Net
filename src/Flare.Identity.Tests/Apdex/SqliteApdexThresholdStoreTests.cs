using Flare.Identity.Apdex;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.Apdex;

public class SqliteApdexThresholdStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteApdexThresholdStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteApdexThresholdStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAllAsync_WithNoOverrides_ReturnsEmpty()
    {
        var overrides = await _store.GetAllAsync();

        Assert.Empty(overrides);
    }

    [Fact]
    public async Task SetAsync_ThenGetAllAsync_ReturnsTheOverride()
    {
        await _store.SetAsync("checkout-api", 250);

        var overrides = await _store.GetAllAsync();

        Assert.Equal(250, Assert.Single(overrides).Value);
        Assert.Equal("checkout-api", Assert.Single(overrides).Key);
    }

    [Fact]
    public async Task SetAsync_CalledTwiceForTheSameService_Upserts()
    {
        await _store.SetAsync("checkout-api", 250);
        await _store.SetAsync("checkout-api", 750);

        var overrides = await _store.GetAllAsync();

        Assert.Equal(750, Assert.Single(overrides).Value);
    }

    [Fact]
    public async Task SetAsync_ForMultipleServices_TracksEachIndependently()
    {
        await _store.SetAsync("checkout-api", 250);
        await _store.SetAsync("search-api", 1000);

        var overrides = await _store.GetAllAsync();

        Assert.Equal(2, overrides.Count);
        Assert.Equal(250, overrides["checkout-api"]);
        Assert.Equal(1000, overrides["search-api"]);
    }

    [Fact]
    public async Task ResetAsync_RemovesTheOverride()
    {
        await _store.SetAsync("checkout-api", 250);

        await _store.ResetAsync("checkout-api");

        Assert.Empty(await _store.GetAllAsync());
    }

    [Fact]
    public async Task ResetAsync_WithNoExistingOverride_IsANoOp()
    {
        await _store.ResetAsync("checkout-api");

        Assert.Empty(await _store.GetAllAsync());
    }
}
