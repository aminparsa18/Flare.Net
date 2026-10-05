using Flare.Api.Auth;
using Flare.Identity.Projects;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Projects;

public class ProjectAccessTests
{
    private static readonly Guid Payments = Guid.NewGuid();
    private static readonly Guid Search = Guid.NewGuid();

    [Fact]
    public void Unrestricted_ReadsAndWritesEverything()
    {
        var access = ProjectAccess.Unrestricted;

        Assert.True(access.IsUnrestricted);
        Assert.True(access.CanRead(Payments));
        Assert.True(access.CanWrite(Payments));
        Assert.True(access.CanManage(Payments));
        Assert.True(access.CanManage(null));
    }

    [Fact]
    public void InstanceWideObjects_AreReadableAndWritable_ByAnyone()
    {
        var access = ProjectAccess.ForMember([]);

        Assert.True(access.CanRead(null));
        Assert.True(access.CanWrite(null));
        Assert.False(access.CanManage(null));
    }

    [Fact]
    public void NonMember_CannotSeeOrChange_AProjectObject()
    {
        var access = ProjectAccess.ForMember([new ProjectMembership(Search, UserRole.Admin)]);

        Assert.False(access.CanRead(Payments));
        Assert.False(access.CanWrite(Payments));
        Assert.False(access.CanManage(Payments));
    }

    [Theory]
    [InlineData(UserRole.Admin, true, true, true)]
    [InlineData(UserRole.Member, true, true, false)]
    [InlineData(UserRole.Viewer, true, false, false)]
    public void MemberRole_DecidesWriteAndManage(UserRole role, bool read, bool write, bool manage)
    {
        var access = ProjectAccess.ForMember([new ProjectMembership(Payments, role)]);

        Assert.Equal(read, access.CanRead(Payments));
        Assert.Equal(write, access.CanWrite(Payments));
        Assert.Equal(manage, access.CanManage(Payments));
    }

    [Fact]
    public void Filter_KeepsInstanceWideAndMemberProjects_Only()
    {
        var access = ProjectAccess.ForMember([new ProjectMembership(Payments, UserRole.Viewer)]);
        (string Name, Guid? Project)[] items = [("a", null), ("b", Payments), ("c", Search)];

        var visible = access.Filter(items, i => i.Project).Select(i => i.Name);

        Assert.Equal(["a", "b"], visible);
        Assert.Equal(3, ProjectAccess.Unrestricted.Filter(items, i => i.Project).Count);
    }

    [Fact]
    public void Normalize_TreatsEmptyGuidAsNone()
    {
        Assert.Null(ProjectGuard.Normalize(null));
        Assert.Null(ProjectGuard.Normalize(Guid.Empty));
        Assert.Equal(Payments, ProjectGuard.Normalize(Payments));
    }

    [Fact]
    public void ResolveForUpdate_OmittedKeepsExisting_EmptyClears_ValueMoves()
    {
        Assert.Equal(Payments, ProjectGuard.ResolveForUpdate(Payments, null));
        Assert.Null(ProjectGuard.ResolveForUpdate(Payments, Guid.Empty));
        Assert.Equal(Search, ProjectGuard.ResolveForUpdate(Payments, Search));
        Assert.Null(ProjectGuard.ResolveForUpdate(null, null));
    }
}
