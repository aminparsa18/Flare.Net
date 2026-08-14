using Flare.Api.Endpoints;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="ProxyAuthLoginEndpoints.ResolveRole"/> - the pure group-header
/// parsing/matching logic - directly, same "pure logic only, no live proxy" scope
/// <see cref="EntraAuthEndpointsTests"/>/<see cref="OidcAuthEndpointsTests"/> already
/// established for their own role resolution. <c>HandleLoginAsync</c> itself isn't
/// unit-tested here - it needs a real <see cref="HttpContext"/> with a configured remote
/// IP and headers, which only a live proxy round-trip (or a much heavier
/// WebApplicationFactory-based test) produces - see docs/auth.md's manual verification
/// steps.
/// </summary>
public class ProxyAuthLoginEndpointsTests
{
    private static ProxyAuthSettings SettingsWithGroups(string? admin, string? member, string? viewer, UserRole defaultRole = UserRole.Viewer) =>
        new(true, "Remote-User", "172.18.0.0/16", "X-Forwarded-Groups", admin, member, viewer, defaultRole, DateTimeOffset.UtcNow);

    [Fact]
    public void ResolveRole_PicksAdmin_WhenMultipleGroupsPresent()
    {
        var settings = SettingsWithGroups("admins", "members", "viewers");

        Assert.Equal(UserRole.Admin, ProxyAuthLoginEndpoints.ResolveRole("viewers,admins,members", settings));
    }

    [Fact]
    public void ResolveRole_PicksMember_WhenNoAdminGroupPresent()
    {
        var settings = SettingsWithGroups("admins", "members", "viewers");

        Assert.Equal(UserRole.Member, ProxyAuthLoginEndpoints.ResolveRole("viewers,members", settings));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenNoRecognizedGroupPresent()
    {
        var settings = SettingsWithGroups("admins", "members", "viewers", UserRole.Viewer);

        Assert.Equal(UserRole.Viewer, ProxyAuthLoginEndpoints.ResolveRole("some-other-group", settings));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenGroupsHeaderValueIsNull()
    {
        var settings = SettingsWithGroups("admins", "members", "viewers", UserRole.Member);

        Assert.Equal(UserRole.Member, ProxyAuthLoginEndpoints.ResolveRole(null, settings));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenGroupsHeaderNameIsNotConfigured()
    {
        // GroupsHeaderName null - default-role-only mode, same as never having a groups
        // header at all.
        var settings = new ProxyAuthSettings(true, "Remote-User", "172.18.0.0/16", null, "admins", "members", "viewers", UserRole.Viewer, DateTimeOffset.UtcNow);

        Assert.Equal(UserRole.Viewer, ProxyAuthLoginEndpoints.ResolveRole(null, settings));
    }

    [Fact]
    public void ResolveRole_MatchesGroupNames_CaseInsensitively()
    {
        var settings = SettingsWithGroups("Admins", null, null);

        Assert.Equal(UserRole.Admin, ProxyAuthLoginEndpoints.ResolveRole("ADMINS", settings));
    }

    [Fact]
    public void ResolveRole_TrimsWhitespaceAroundEachGroupEntry()
    {
        var settings = SettingsWithGroups("admins", null, null);

        Assert.Equal(UserRole.Admin, ProxyAuthLoginEndpoints.ResolveRole(" viewers , admins ", settings));
    }
}
