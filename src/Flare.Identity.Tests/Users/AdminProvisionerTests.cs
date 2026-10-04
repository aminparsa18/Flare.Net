using Flare.Identity.Auth;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Identity.Tests.Users;

public class AdminProvisionerTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbUserStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    private Task Apply(AdminProvisioningOptions o) => AdminProvisioner.ApplyAsync(_store, o, NullLogger.Instance);

    [Fact]
    public async Task CreatesAdmin_WhenNoUsersExist()
    {
        await Apply(new() { Username = "root", Password = "correct horse" });

        var user = await _store.VerifyPasswordAsync("root", "correct horse");
        Assert.NotNull(user);
        Assert.Equal(UserRole.Admin, user.Role);
    }

    [Fact]
    public async Task ReadsPasswordFile_AndTrimsTrailingNewline()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "from-a-file\n");
            await Apply(new() { Username = "root", PasswordFile = path, Password = "ignored-value" });
            Assert.NotNull(await _store.VerifyPasswordAsync("root", "from-a-file"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DoesNothing_WhenAnyUserExists()
    {
        await _store.CreateAsync("alice", "alice-password", UserRole.Admin);
        await Apply(new() { Username = "root", Password = "correct horse" });
        Assert.Null(await _store.FindByUsernameAsync("root"));
    }

    [Fact]
    public async Task DoesNotChangePassword_WithoutReconcile()
    {
        await _store.CreateAsync("root", "original-pass", UserRole.Admin);
        await Apply(new() { Username = "root", Password = "different-pass" });
        Assert.NotNull(await _store.VerifyPasswordAsync("root", "original-pass"));
    }

    [Fact]
    public async Task Reconcile_ResetsPassword_Role_AndEnabled()
    {
        var u = await _store.CreateAsync("root", "original-pass", UserRole.Admin);
        await _store.SetRoleAsync(u.Id, UserRole.Viewer);
        await _store.SetDisabledAsync(u.Id, true);

        await Apply(new() { Username = "root", Password = "different-pass", Reconcile = true });

        var after = await _store.VerifyPasswordAsync("root", "different-pass");
        Assert.NotNull(after);
        Assert.Equal(UserRole.Admin, after.Role);
        Assert.Null(await _store.VerifyPasswordAsync("root", "original-pass"));
    }

    [Fact]
    public async Task SkipsShortPassword_AndIncompleteConfig()
    {
        await Apply(new() { Username = "root", Password = "short" });
        await Apply(new() { Username = "root" });
        Assert.False(await _store.AnyAsync());
    }
}
