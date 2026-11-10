using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Auth;

public class DbSessionStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbSessionStore _store = null!;
    private DbUserStore _userStore = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbSessionStore(_database.ConnectionFactory, TimeProvider.System);
        _userStore = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    // Sessions.UserId REFERENCES Users(Id) (Migrations/0001_identity.sql) and
    // Microsoft.Data.Sqlite enables PRAGMA foreign_keys=ON by default - every session in
    // these tests needs a real Users row behind it, not an arbitrary Guid.
    private async Task<Guid> CreateUserAsync(string username) =>
        (await _userStore.CreateAsync(username, "correct horse battery staple", UserRole.Viewer)).Id;

    [Fact]
    public async Task CreateAsync_ThenFindAsync_ReturnsTheSession()
    {
        var userId = await CreateUserAsync("alice");

        var created = await _store.CreateAsync(userId, TimeSpan.FromDays(14), TestContext.Current.CancellationToken);
        var found = await _store.FindAsync(created.Token!, TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(userId, found.UserId);
        Assert.Equal(created.Id, found.Id);
    }

    [Fact]
    public async Task CreateAsync_StoresOnlyTheTokenDigest()
    {
        var userId = await CreateUserAsync("carol");

        var created = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);

        Assert.NotNull(created.Token);
        Assert.Equal(SessionTokenHasher.Hash(created.Token), created.Id);
        Assert.NotEqual(created.Token, created.Id);

        // The digest is not itself a usable credential, and a found/listed session never carries the token.
        Assert.Null(await _store.FindAsync(created.Id, TestContext.Current.CancellationToken));
        Assert.Null((await _store.FindAsync(created.Token, TestContext.Current.CancellationToken))!.Token);
        Assert.Null(Assert.Single(await _store.ListForUserAsync(userId, TestContext.Current.CancellationToken)).Token);
    }

    [Fact]
    public async Task DeleteByIdAsync_RevokesTheSession()
    {
        var userId = await CreateUserAsync("dave");
        var created = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);

        await _store.DeleteByIdAsync(created.Id, TestContext.Current.CancellationToken);

        Assert.Null(await _store.FindAsync(created.Token!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_GeneratesADifferentTokenEachTime()
    {
        var userId = await CreateUserAsync("bob");

        var first = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);
        var second = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_ForUnknownToken()
    {
        Assert.Null(await _store.FindAsync("not-a-real-token", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_ForAnAlreadyExpiredSession()
    {
        var userId = await CreateUserAsync("carol");

        var session = await _store.CreateAsync(userId, TimeSpan.FromMilliseconds(-1), TestContext.Current.CancellationToken);

        Assert.Null(await _store.FindAsync(session.Token!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RevokesTheSessionImmediately()
    {
        var userId = await CreateUserAsync("dave");

        var session = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);
        await _store.DeleteAsync(session.Token!, TestContext.Current.CancellationToken);

        Assert.Null(await _store.FindAsync(session.Token!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAllForUserAsync_RevokesEveryOneOfThatUsersSessions_ButNotOthers()
    {
        var userId = await CreateUserAsync("erin");
        var otherUserId = await CreateUserAsync("frank");
        var sessionA = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);
        var sessionB = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);
        var otherSession = await _store.CreateAsync(otherUserId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);

        await _store.DeleteAllForUserAsync(userId, TestContext.Current.CancellationToken);

        Assert.Null(await _store.FindAsync(sessionA.Token!, TestContext.Current.CancellationToken));
        Assert.Null(await _store.FindAsync(sessionB.Token!, TestContext.Current.CancellationToken));
        Assert.NotNull(await _store.FindAsync(otherSession.Token!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TouchLastSeenAsync_AdvancesLastSeenAt()
    {
        var userId = await CreateUserAsync("grace");

        var session = await _store.CreateAsync(userId, TimeSpan.FromDays(1), TestContext.Current.CancellationToken);

        await Task.Delay(10, TestContext.Current.CancellationToken); // ensure the clock has moved at all
        await _store.TouchLastSeenAsync(session.Token!, TestContext.Current.CancellationToken);

        var found = await _store.FindAsync(session.Token!, TestContext.Current.CancellationToken);
        Assert.True(found!.LastSeenAt > session.LastSeenAt);
    }
}
