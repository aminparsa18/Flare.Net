using Flare.Identity.Auth;
using Flare.Identity.Projects;
using Flare.Identity.Tests.TestSupport;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Identity.Tests.Projects;

public class DbProjectStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private DbProjectStore _store = null!;
    private DbUserStore _users = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbProjectStore(_database.ConnectionFactory, TimeProvider.System);
        _users = new DbUserStore(_database.ConnectionFactory, new AspNetPasswordHasher(), TimeProvider.System);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CreateAsync_RoundTripsPatternsSortedAndDeduplicated()
    {
        var created = await _store.CreateAsync("Payments", "pay team", ["pay-*", "billing", "pay-*"]);

        var read = await _store.GetAsync(created.Id);
        Assert.NotNull(read);
        Assert.Equal("Payments", read.Name);
        Assert.Equal(["billing", "pay-*"], read.ServicePatterns);
        Assert.Equal(created.Id, Assert.Single(await _store.ListAsync()).Id);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameIgnoringCase_Throws()
    {
        await _store.CreateAsync("Payments", "", []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.CreateAsync("payments", "", []));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesPatterns_AndAllowsKeepingOwnName()
    {
        var p = await _store.CreateAsync("A", "", ["x"]);
        await _store.CreateAsync("B", "", []);

        var updated = await _store.UpdateAsync(p.Id, "A", "d", ["y-*"]);

        Assert.Equal(["y-*"], updated!.ServicePatterns);
        Assert.Equal("d", updated.Description);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.UpdateAsync(p.Id, "b", "", []));
        Assert.Null(await _store.UpdateAsync(Guid.NewGuid(), "Z", "", []));
    }

    [Fact]
    public async Task Members_SetChangeListRemove()
    {
        var p = await _store.CreateAsync("A", "", []);
        var alice = await _users.CreateAsync("alice", "alice-password", UserRole.Viewer);

        Assert.True(await _store.SetMemberAsync(p.Id, alice.Id, UserRole.Member));
        Assert.True(await _store.SetMemberAsync(p.Id, alice.Id, UserRole.Admin));

        var member = Assert.Single(await _store.ListMembersAsync(p.Id));
        Assert.Equal(("alice", UserRole.Admin), (member.Username, member.Role));
        Assert.Equal(new ProjectMembership(p.Id, UserRole.Admin), Assert.Single(await _store.ListMembershipsAsync(alice.Id)));

        Assert.True(await _store.RemoveMemberAsync(p.Id, alice.Id));
        Assert.False(await _store.RemoveMemberAsync(p.Id, alice.Id));
        Assert.Empty(await _store.ListMembersAsync(p.Id));
    }

    [Fact]
    public async Task SetMemberAsync_UnknownProjectOrUser_ReturnsFalse()
    {
        var p = await _store.CreateAsync("A", "", []);
        var alice = await _users.CreateAsync("alice", "alice-password", UserRole.Viewer);

        Assert.False(await _store.SetMemberAsync(Guid.NewGuid(), alice.Id, UserRole.Member));
        Assert.False(await _store.SetMemberAsync(p.Id, Guid.NewGuid(), UserRole.Member));
    }

    [Fact]
    public async Task DeleteAsync_RemovesPatternsAndMembers()
    {
        var p = await _store.CreateAsync("A", "", ["x"]);
        var alice = await _users.CreateAsync("alice", "alice-password", UserRole.Viewer);
        await _store.SetMemberAsync(p.Id, alice.Id, UserRole.Member);

        Assert.True(await _store.DeleteAsync(p.Id));
        Assert.False(await _store.DeleteAsync(p.Id));

        Assert.Null(await _store.GetAsync(p.Id));
        Assert.Empty(await _store.ListMembershipsAsync(alice.Id));
    }
}
