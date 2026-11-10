using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Users;

public class DbUserStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbUserStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AnyAsync_ReturnsFalse_WhenNoUsersExist()
    {
        Assert.False(await _store.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnyAsync_ReturnsTrue_AfterCreatingAUser()
    {
        await _store.CreateAsync("alice", "correct horse battery staple", UserRole.Admin, TestContext.Current.CancellationToken);

        Assert.True(await _store.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_ThenFindByUsername_ReturnsTheUser_WithoutExposingPasswordHash()
    {
        var created = await _store.CreateAsync("alice", "correct horse battery staple", UserRole.Admin, TestContext.Current.CancellationToken);

        var found = await _store.FindByUsernameAsync("alice", TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(UserRole.Admin, found.Role);
        Assert.False(found.IsDisabled);
    }

    [Fact]
    public async Task FindByUsername_IsCaseInsensitive()
    {
        await _store.CreateAsync("Bob", "hunter2hunter2", UserRole.Viewer, TestContext.Current.CancellationToken);

        Assert.NotNull(await _store.FindByUsernameAsync("bob", TestContext.Current.CancellationToken));
        Assert.NotNull(await _store.FindByUsernameAsync("BOB", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsUser_ForCorrectCredentials()
    {
        await _store.CreateAsync("carol", "correcthorsebattery", UserRole.Member, TestContext.Current.CancellationToken);

        var result = await _store.VerifyPasswordAsync("carol", "correcthorsebattery", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("carol", result.Username);
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForWrongPassword()
    {
        await _store.CreateAsync("dave", "correctpassword1", UserRole.Member, TestContext.Current.CancellationToken);

        Assert.Null(await _store.VerifyPasswordAsync("dave", "wrongpassword1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForUnknownUsername()
    {
        Assert.Null(await _store.VerifyPasswordAsync("no-such-user", "whatever", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForDisabledUser_EvenWithCorrectPassword()
    {
        var user = await _store.CreateAsync("erin", "correctpassword2", UserRole.Viewer, TestContext.Current.CancellationToken);

        await _store.SetDisabledAsync(user.Id, isDisabled: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await _store.VerifyPasswordAsync("erin", "correctpassword2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetRoleAsync_ChangesTheStoredRole()
    {
        var user = await _store.CreateAsync("frank", "correctpassword3", UserRole.Viewer, TestContext.Current.CancellationToken);

        await _store.SetRoleAsync(user.Id, UserRole.Admin, TestContext.Current.CancellationToken);

        var found = await _store.FindByIdAsync(user.Id, TestContext.Current.CancellationToken);
        Assert.Equal(UserRole.Admin, found!.Role);
    }

    [Fact]
    public async Task CreateAsync_ThrowsOnDuplicateUsername()
    {
        await _store.CreateAsync("grace", "correctpassword4", UserRole.Viewer, TestContext.Current.CancellationToken);

        // Users.Username UNIQUE COLLATE NOCASE (Migrations/0001_identity.sql) - the
        // uniqueness constraint (and case-insensitivity of it) is enforced by SQLite
        // itself, not application code, so this exercises that the schema actually says
        // what the migration file claims it does.
        await Assert.ThrowsAnyAsync<Exception>(() => _store.CreateAsync("GRACE", "differentpassword", UserRole.Viewer, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListAsync_ReturnsAllUsers_OrderedByUsername()
    {
        await _store.CreateAsync("zeta", "correctpassword5", UserRole.Viewer, TestContext.Current.CancellationToken);
        await _store.CreateAsync("alpha", "correctpassword6", UserRole.Viewer, TestContext.Current.CancellationToken);

        var list = await _store.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["alpha", "zeta"], list.Select(u => u.Username));
    }

    [Fact]
    public async Task CreateFromExternalAsync_ThenFindByExternalId_ReturnsTheUser()
    {
        var created = await _store.CreateFromExternalAsync("Entra", "oid-123", "heidi@example.com", UserRole.Member, TestContext.Current.CancellationToken);

        Assert.Equal("Entra", created.AuthProvider);
        Assert.Equal("oid-123", created.ExternalId);

        var found = await _store.FindByExternalIdAsync("Entra", "oid-123", TestContext.Current.CancellationToken);
        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(UserRole.Member, found.Role);
    }

    [Fact]
    public async Task FindByExternalIdAsync_ReturnsNull_WhenNoMatch()
    {
        Assert.Null(await _store.FindByExternalIdAsync("Entra", "no-such-oid", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LocalUsers_ReportAuthProviderLocal_WithNoExternalId()
    {
        var created = await _store.CreateAsync("ivan", "correctpassword7", UserRole.Viewer, TestContext.Current.CancellationToken);

        Assert.Equal("Local", created.AuthProvider);
        Assert.Null(created.ExternalId);
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForAnEntraProvisionedAccount_RegardlessOfPassword()
    {
        // An Entra-provisioned account's PasswordHash is a real, well-formed hash of a
        // random string nobody knows - VerifyPasswordAsync must reject it like any wrong
        // password, not throw, for every guess.
        await _store.CreateFromExternalAsync("Entra", "oid-456", "judy@example.com", UserRole.Viewer, TestContext.Current.CancellationToken);

        Assert.Null(await _store.VerifyPasswordAsync("judy@example.com", "anything", TestContext.Current.CancellationToken));
        Assert.Null(await _store.VerifyPasswordAsync("judy@example.com", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateFromExternalAsync_ThrowsOnDuplicateExternalId_ForTheSameProvider()
    {
        await _store.CreateFromExternalAsync("Entra", "oid-789", "mallory@example.com", UserRole.Viewer, TestContext.Current.CancellationToken);

        // UX_Users_AuthProvider_ExternalId (Migrations/0002_entra_id.sql) - enforced by
        // SQLite itself, same precedent as CreateAsync_ThrowsOnDuplicateUsername above.
        await Assert.ThrowsAnyAsync<Exception>(() => _store.CreateFromExternalAsync("Entra", "oid-789", "mallory2@example.com", UserRole.Viewer, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateServiceAccountAsync_IsAServiceAccount_AndCanNeverPassPasswordLogin()
    {
        var created = await _store.CreateServiceAccountAsync("ci-bot", UserRole.Viewer, TestContext.Current.CancellationToken);

        Assert.True(created.IsServiceAccount);
        Assert.Equal("ServiceAccount", created.AuthProvider);
        var found = await _store.FindByUsernameAsync("ci-bot", TestContext.Current.CancellationToken);
        Assert.True(found!.IsServiceAccount);
        Assert.Equal(UserRole.Viewer, found.Role);
        Assert.Null(await _store.VerifyPasswordAsync("ci-bot", "", TestContext.Current.CancellationToken));
        Assert.Null(await _store.VerifyPasswordAsync("ci-bot", "ci-bot", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServiceAccount_IsDisabledLikeAnyUser()
    {
        var created = await _store.CreateServiceAccountAsync("ci-bot", UserRole.Member, TestContext.Current.CancellationToken);

        await _store.SetDisabledAsync(created.Id, true, TestContext.Current.CancellationToken);

        Assert.True((await _store.FindByIdAsync(created.Id, TestContext.Current.CancellationToken))!.IsDisabled);
    }

    [Fact]
    public async Task DeleteServiceAccountAsync_RemovesTheAccountAndItsTokens()
    {
        var account = await _store.CreateServiceAccountAsync("ci", UserRole.Member, TestContext.Current.CancellationToken);
        var tokens = new Flare.Identity.PersonalAccessTokens.DbPersonalAccessTokenStore(_database.ConnectionFactory, TimeProvider.System);
        await tokens.CreateAsync(account.Id, "deploy", null, TestContext.Current.CancellationToken);

        Assert.True(await _store.DeleteServiceAccountAsync(account.Id, TestContext.Current.CancellationToken));

        Assert.Null(await _store.FindByIdAsync(account.Id, TestContext.Current.CancellationToken));
        Assert.Empty(await tokens.ListForUserAsync(account.Id, TestContext.Current.CancellationToken));
        Assert.False(await _store.DeleteServiceAccountAsync(account.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteServiceAccountAsync_NeverDeletesAHumanAccount()
    {
        var human = await _store.CreateAsync("alice", "alice-password", UserRole.Admin, TestContext.Current.CancellationToken);

        Assert.False(await _store.DeleteServiceAccountAsync(human.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await _store.FindByIdAsync(human.Id, TestContext.Current.CancellationToken));
    }
}
