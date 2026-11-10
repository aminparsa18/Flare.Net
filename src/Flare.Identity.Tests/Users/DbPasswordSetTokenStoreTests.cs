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

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _tokens = new DbPasswordSetTokenStore(_database.ConnectionFactory, TimeProvider.System);
        _users = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
        _sessions = new DbSessionStore(_database.ConnectionFactory, TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Consume_ReturnsUserOnce()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer, TestContext.Current.CancellationToken);
        var t = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Invite, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        Assert.Equal(user.Id, await _tokens.ConsumeAsync(t.RawToken, TestContext.Current.CancellationToken));
        Assert.Null(await _tokens.ConsumeAsync(t.RawToken, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryCreate_ThrottlesWithinInterval_ThenAllowsAgain()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer, TestContext.Current.CancellationToken);
        var first = await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.NotNull(first);

        Assert.Null(await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        // The first token must survive a throttled attempt.
        Assert.Equal(user.Id, await _tokens.ConsumeAsync(first!.RawToken, TestContext.Current.CancellationToken));

        var second = await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.NotNull(second);
        // A zero interval always replaces the previous token.
        var third = await _tokens.TryCreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TimeSpan.Zero, TestContext.Current.CancellationToken);
        Assert.NotNull(third);
        Assert.Null(await _tokens.ConsumeAsync(second!.RawToken, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Consume_RejectsExpiredAndUnknownTokens()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer, TestContext.Current.CancellationToken);
        var t = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromSeconds(-1), TestContext.Current.CancellationToken);

        Assert.Null(await _tokens.ConsumeAsync(t.RawToken, TestContext.Current.CancellationToken));
        Assert.Null(await _tokens.ConsumeAsync("nope", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ReplacesThePreviousTokenForTheUser()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer, TestContext.Current.CancellationToken);
        var first = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);
        var second = await _tokens.CreateAsync(user.Id, PasswordSetPurpose.Reset, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        Assert.Null(await _tokens.ConsumeAsync(first.RawToken, TestContext.Current.CancellationToken));
        Assert.Equal(user.Id, await _tokens.ConsumeAsync(second.RawToken, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetPassword_ReplacesTheOldPassword()
    {
        var user = await _users.CreateAsync("a", "old-password-1", UserRole.Viewer, TestContext.Current.CancellationToken);
        await _users.SetPasswordAsync(user.Id, "new-password-1", TestContext.Current.CancellationToken);

        Assert.Null(await _users.VerifyPasswordAsync("a", "old-password-1", TestContext.Current.CancellationToken));
        Assert.NotNull(await _users.VerifyPasswordAsync("a", "new-password-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAllForUserExcept_KeepsOnlyTheNamedSession()
    {
        var user = await _users.CreateAsync("a", "password-123", UserRole.Viewer, TestContext.Current.CancellationToken);
        var keep = await _sessions.CreateAsync(user.Id, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);
        var drop = await _sessions.CreateAsync(user.Id, TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        await _sessions.DeleteAllForUserExceptAsync(user.Id, keep.Token!, TestContext.Current.CancellationToken);

        Assert.NotNull(await _sessions.FindAsync(keep.Token!, TestContext.Current.CancellationToken));
        Assert.Null(await _sessions.FindAsync(drop.Token!, TestContext.Current.CancellationToken));
    }
}
