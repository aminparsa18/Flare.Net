using Flare.Identity.DashboardPins;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.DashboardPins;

public class SqliteDashboardPinStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private readonly FakeClock _clock = new();
    private SqliteDashboardPinStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteDashboardPinStore(_database.ConnectionFactory, _clock);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ListAsync_ReturnsMostRecentlyPinnedFirst()
    {
        var user = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _store.PinAsync(user, first);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _store.PinAsync(user, second);

        Assert.Equal([second, first], await _store.ListAsync(user));
    }

    [Fact]
    public async Task PinAsync_IsIdempotentAndScopedPerUser()
    {
        var dashboard = Guid.NewGuid();
        var alice = Guid.NewGuid();
        await _store.PinAsync(alice, dashboard);
        await _store.PinAsync(alice, dashboard);

        Assert.Single(await _store.ListAsync(alice));
        Assert.Empty(await _store.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UnpinAsync_RemovesOnlyThatPin()
    {
        var user = Guid.NewGuid();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        await _store.PinAsync(user, keep);
        await _store.PinAsync(user, drop);

        await _store.UnpinAsync(user, drop);
        await _store.UnpinAsync(user, Guid.NewGuid()); // not pinned: a no-op

        Assert.Equal([keep], await _store.ListAsync(user));
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
