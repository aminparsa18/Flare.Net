using Flare.Identity.Tests.TestSupport;
using Flare.Identity.UserPreferences;
using Xunit;

namespace Flare.Identity.Tests.UserPreferences;

public class DbUserPreferencesStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbUserPreferencesStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbUserPreferencesStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNothingStored()
    {
        Assert.Null(await _store.GetAsync(Guid.NewGuid(), "appearance", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAsync_ReplacesExistingValueAndIsScopedPerUser()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await _store.SetAsync(alice, "appearance", """{"density":"compact"}""", TestContext.Current.CancellationToken);
        await _store.SetAsync(alice, "appearance", """{"density":"comfortable"}""", TestContext.Current.CancellationToken);
        await _store.SetAsync(bob, "appearance", """{"density":"compact"}""", TestContext.Current.CancellationToken);

        Assert.Equal("""{"density":"comfortable"}""", await _store.GetAsync(alice, "appearance", TestContext.Current.CancellationToken));
        Assert.Equal("""{"density":"compact"}""", await _store.GetAsync(bob, "appearance", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatKeyForThatUser()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await _store.SetAsync(alice, "appearance", "{}", TestContext.Current.CancellationToken);
        await _store.SetAsync(bob, "appearance", "{}", TestContext.Current.CancellationToken);

        await _store.DeleteAsync(alice, "appearance", TestContext.Current.CancellationToken);

        Assert.Null(await _store.GetAsync(alice, "appearance", TestContext.Current.CancellationToken));
        Assert.NotNull(await _store.GetAsync(bob, "appearance", TestContext.Current.CancellationToken));
    }
}
