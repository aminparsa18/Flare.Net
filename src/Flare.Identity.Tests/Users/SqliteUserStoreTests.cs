using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Users;

public class SqliteUserStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private SqliteUserStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AnyAsync_ReturnsFalse_WhenNoUsersExist()
    {
        Assert.False(await _store.AnyAsync());
    }

    [Fact]
    public async Task AnyAsync_ReturnsTrue_AfterCreatingAUser()
    {
        await _store.CreateAsync("alice", "correct horse battery staple", UserRole.Admin);

        Assert.True(await _store.AnyAsync());
    }

    [Fact]
    public async Task CreateAsync_ThenFindByUsername_ReturnsTheUser_WithoutExposingPasswordHash()
    {
        var created = await _store.CreateAsync("alice", "correct horse battery staple", UserRole.Admin);

        var found = await _store.FindByUsernameAsync("alice");

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(UserRole.Admin, found.Role);
        Assert.False(found.IsDisabled);
    }

    [Fact]
    public async Task FindByUsername_IsCaseInsensitive()
    {
        await _store.CreateAsync("Bob", "hunter2hunter2", UserRole.Viewer);

        Assert.NotNull(await _store.FindByUsernameAsync("bob"));
        Assert.NotNull(await _store.FindByUsernameAsync("BOB"));
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsUser_ForCorrectCredentials()
    {
        await _store.CreateAsync("carol", "correcthorsebattery", UserRole.Member);

        var result = await _store.VerifyPasswordAsync("carol", "correcthorsebattery");

        Assert.NotNull(result);
        Assert.Equal("carol", result.Username);
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForWrongPassword()
    {
        await _store.CreateAsync("dave", "correctpassword1", UserRole.Member);

        Assert.Null(await _store.VerifyPasswordAsync("dave", "wrongpassword1"));
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForUnknownUsername()
    {
        Assert.Null(await _store.VerifyPasswordAsync("no-such-user", "whatever"));
    }

    [Fact]
    public async Task VerifyPasswordAsync_ReturnsNull_ForDisabledUser_EvenWithCorrectPassword()
    {
        var user = await _store.CreateAsync("erin", "correctpassword2", UserRole.Viewer);

        await _store.SetDisabledAsync(user.Id, isDisabled: true);

        Assert.Null(await _store.VerifyPasswordAsync("erin", "correctpassword2"));
    }

    [Fact]
    public async Task SetRoleAsync_ChangesTheStoredRole()
    {
        var user = await _store.CreateAsync("frank", "correctpassword3", UserRole.Viewer);

        await _store.SetRoleAsync(user.Id, UserRole.Admin);

        var found = await _store.FindByIdAsync(user.Id);
        Assert.Equal(UserRole.Admin, found!.Role);
    }

    [Fact]
    public async Task CreateAsync_ThrowsOnDuplicateUsername()
    {
        await _store.CreateAsync("grace", "correctpassword4", UserRole.Viewer);

        // Users.Username UNIQUE COLLATE NOCASE (Migrations/0001_identity.sql) - the
        // uniqueness constraint (and case-insensitivity of it) is enforced by SQLite
        // itself, not application code, so this exercises that the schema actually says
        // what the migration file claims it does.
        await Assert.ThrowsAnyAsync<Exception>(() => _store.CreateAsync("GRACE", "differentpassword", UserRole.Viewer));
    }

    [Fact]
    public async Task ListAsync_ReturnsAllUsers_OrderedByUsername()
    {
        await _store.CreateAsync("zeta", "correctpassword5", UserRole.Viewer);
        await _store.CreateAsync("alpha", "correctpassword6", UserRole.Viewer);

        var list = await _store.ListAsync();

        Assert.Equal(["alpha", "zeta"], list.Select(u => u.Username));
    }
}
