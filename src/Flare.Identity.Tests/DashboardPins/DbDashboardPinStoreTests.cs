using Flare.Identity.DashboardPins;
using Flare.Identity.Tests.TestSupport;
using Xunit;

namespace Flare.Identity.Tests.DashboardPins;

public class DbDashboardPinStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private readonly FakeClock _clock = new();
    private DbDashboardPinStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbDashboardPinStore(_database.ConnectionFactory, _clock);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ListAsync_ReturnsMostRecentlyPinnedFirst()
    {
        var user = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _store.PinAsync(user, first, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _store.PinAsync(user, second, TestContext.Current.CancellationToken);

        Assert.Equal([second, first], await _store.ListAsync(user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PinAsync_IsIdempotentAndScopedPerUser()
    {
        var dashboard = Guid.NewGuid();
        var alice = Guid.NewGuid();
        await _store.PinAsync(alice, dashboard, TestContext.Current.CancellationToken);
        await _store.PinAsync(alice, dashboard, TestContext.Current.CancellationToken);

        Assert.Single(await _store.ListAsync(alice, TestContext.Current.CancellationToken));
        Assert.Empty(await _store.ListAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnpinAsync_RemovesOnlyThatPin()
    {
        var user = Guid.NewGuid();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        await _store.PinAsync(user, keep, TestContext.Current.CancellationToken);
        await _store.PinAsync(user, drop, TestContext.Current.CancellationToken);

        await _store.UnpinAsync(user, drop, TestContext.Current.CancellationToken);
        await _store.UnpinAsync(user, Guid.NewGuid(), TestContext.Current.CancellationToken); // not pinned: a no-op

        Assert.Equal([keep], await _store.ListAsync(user, TestContext.Current.CancellationToken));
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
