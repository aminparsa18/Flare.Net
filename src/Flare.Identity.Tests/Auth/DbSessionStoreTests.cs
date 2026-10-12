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

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbSessionStore(_database.ConnectionFactory, TimeProvider.System);
        _userStore = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    // Sessions.UserId REFERENCES Users(Id) (Migrations/0001_identity.sql) and
    // Microsoft.Data.Sqlite enables PRAGMA foreign_keys=ON by default - every session in
    // these tests needs a real Users row behind it, not an arbitrary Guid.
    private async Task<Guid> CreateUserAsync(string username) =>
        (await _userStore.CreateAsync(username, "correct horse battery staple", UserRole.Viewer)).Id;

    [Fact]
    public async Task CreateAsync_ThenFindAsync_ReturnsTheSession()
    {
        var userId = await CreateUserAsync("alice");

        var created = await _store.CreateAsync(userId, TimeSpan.FromDays(14));
        var found = await _store.FindAsync(created.Token!);

        Assert.NotNull(found);
        Assert.Equal(userId, found.UserId);
        Assert.Equal(created.Id, found.Id);
    }

    [Fact]
    public async Task CreateAsync_StoresOnlyTheTokenDigest()
    {
        var userId = await CreateUserAsync("carol");

        var created = await _store.CreateAsync(userId, TimeSpan.FromDays(1));

        Assert.NotNull(created.Token);
        Assert.Equal(SessionTokenHasher.Hash(created.Token), created.Id);
        Assert.NotEqual(created.Token, created.Id);

        // The digest is not itself a usable credential, and a found/listed session never carries the token.
        Assert.Null(await _store.FindAsync(created.Id));
        Assert.Null((await _store.FindAsync(created.Token))!.Token);
        Assert.Null(Assert.Single(await _store.ListForUserAsync(userId)).Token);
    }

    [Fact]
    public async Task DeleteByIdAsync_RevokesTheSession()
    {
        var userId = await CreateUserAsync("dave");
        var created = await _store.CreateAsync(userId, TimeSpan.FromDays(1));

        await _store.DeleteByIdAsync(created.Id);

        Assert.Null(await _store.FindAsync(created.Token!));
    }

    [Fact]
    public async Task CreateAsync_GeneratesADifferentTokenEachTime()
    {
        var userId = await CreateUserAsync("bob");

        var first = await _store.CreateAsync(userId, TimeSpan.FromDays(1));
        var second = await _store.CreateAsync(userId, TimeSpan.FromDays(1));

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_ForUnknownToken()
    {
        Assert.Null(await _store.FindAsync("not-a-real-token"));
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_ForAnAlreadyExpiredSession()
    {
        var userId = await CreateUserAsync("carol");

        var session = await _store.CreateAsync(userId, TimeSpan.FromMilliseconds(-1));

        Assert.Null(await _store.FindAsync(session.Token!));
    }

    [Fact]
    public async Task DeleteAsync_RevokesTheSessionImmediately()
    {
        var userId = await CreateUserAsync("dave");

        var session = await _store.CreateAsync(userId, TimeSpan.FromDays(1));
        await _store.DeleteAsync(session.Token!);

        Assert.Null(await _store.FindAsync(session.Token!));
    }

    [Fact]
    public async Task DeleteAllForUserAsync_RevokesEveryOneOfThatUsersSessions_ButNotOthers()
    {
        var userId = await CreateUserAsync("erin");
        var otherUserId = await CreateUserAsync("frank");
        var sessionA = await _store.CreateAsync(userId, TimeSpan.FromDays(1));
        var sessionB = await _store.CreateAsync(userId, TimeSpan.FromDays(1));
        var otherSession = await _store.CreateAsync(otherUserId, TimeSpan.FromDays(1));

        await _store.DeleteAllForUserAsync(userId);

        Assert.Null(await _store.FindAsync(sessionA.Token!));
        Assert.Null(await _store.FindAsync(sessionB.Token!));
        Assert.NotNull(await _store.FindAsync(otherSession.Token!));
    }

    [Fact]
    public async Task TouchLastSeenAsync_AdvancesLastSeenAt()
    {
        var userId = await CreateUserAsync("grace");

        var session = await _store.CreateAsync(userId, TimeSpan.FromDays(1));

        await Task.Delay(10); // ensure the clock has moved at all
        await _store.TouchLastSeenAsync(session.Token!);

        var found = await _store.FindAsync(session.Token!);
        Assert.True(found!.LastSeenAt > session.LastSeenAt);
    }
}
