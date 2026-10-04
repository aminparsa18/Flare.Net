using Flare.Identity.Auth;
using Flare.Identity.PasswordSetTokens;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Users;

public class DbPasswordSetTokenStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbPasswordSetTokenStore _tokens = null!;
    private DbUserStore _users = null!;
    private DbSessionStore _sessions = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _tokens = new DbPasswordSetTokenStore(_database.ConnectionFactory, TimeProvider.System);
        _users = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
        _sessions = new DbSessionStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Consume_ReturnsUserOnce()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer);
        var t = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Invite, TimeSpan.FromHours(1));

        Assert.Equal(user.Id, await _tokens.ConsumeAsync(t.RawToken));
        Assert.Null(await _tokens.ConsumeAsync(t.RawToken));
    }

    [Fact]
    public async Task TryCreate_ThrottlesWithinInterval_ThenAllowsAgain()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer);
        var first = await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));
        Assert.NotNull(first);

        Assert.Null(await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1)));
        // The first token must survive a throttled attempt.
        Assert.Equal(user.Id, await _tokens.ConsumeAsync(first!.RawToken));

        var second = await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));
        Assert.NotNull(second);
        // A zero interval always replaces the previous token.
        var third = await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.Zero);
        Assert.NotNull(third);
        Assert.Null(await _tokens.ConsumeAsync(second!.RawToken));
    }

    [Fact]
    public async Task Consume_RejectsExpiredAndUnknownTokens()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer);
        var t = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromSeconds(-1));

        Assert.Null(await _tokens.ConsumeAsync(t.RawToken));
        Assert.Null(await _tokens.ConsumeAsync("nope"));
    }

    [Fact]
    public async Task Create_ReplacesThePreviousTokenForTheUser()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer);
        var first = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1));
        var second = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1));

        Assert.Null(await _tokens.ConsumeAsync(first.RawToken));
        Assert.Equal(user.Id, await _tokens.ConsumeAsync(second.RawToken));
    }

    [Fact]
    public async Task SetPassword_ReplacesTheOldPassword()
    {
        var user = await _users.CreateAsync("a", "old-password-1", UserRole.Viewer);
        await _users.SetPasswordAsync(user.Id, "new-password-1");

        Assert.Null(await _users.VerifyPasswordAsync("a", "old-password-1"));
        Assert.NotNull(await _users.VerifyPasswordAsync("a", "new-password-1"));
    }

    [Fact]
    public async Task DeleteAllForUserExcept_KeepsOnlyTheNamedSession()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer);
        var keep = await _sessions.CreateAsync(user.Id, TimeSpan.FromHours(1));
        var drop = await _sessions.CreateAsync(user.Id, TimeSpan.FromHours(1));

        await _sessions.DeleteAllForUserExceptAsync(user.Id, keep.Id);

        Assert.NotNull(await _sessions.FindAsync(keep.Id));
        Assert.Null(await _sessions.FindAsync(drop.Id));
    }
}
