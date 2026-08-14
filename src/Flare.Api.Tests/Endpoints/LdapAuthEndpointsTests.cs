using Flare.Api.Endpoints;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="LdapAuthEndpoints.ResolveRole"/> - the one piece of the LDAP
/// login flow that's pure and testable without a real directory. The actual bind/search
/// LDAP client code isn't unit-tested here - left to e2e (a throwaway OpenLDAP
/// container - see docs/auth.md), same "left to e2e only" convention this repo already
/// uses for e.g. <c>RedisStreamLogEventSink</c>/<c>ClickHouseFlushWorker</c>.
/// </summary>
public class LdapAuthEndpointsTests
{
    private static LdapSettings SettingsWithGroups(string? adminGroupDn = null, string? memberGroupDn = null, string? viewerGroupDn = null, UserRole defaultRole = UserRole.Viewer) =>
        LdapSettings.NotConfigured with
        {
            AdminGroupDn = adminGroupDn,
            MemberGroupDn = memberGroupDn,
            ViewerGroupDn = viewerGroupDn,
            DefaultRole = defaultRole,
        };

    [Fact]
    public void ResolveRole_PicksAdmin_WhenMemberOfMultipleMappedGroups()
    {
        var settings = SettingsWithGroups(
            adminGroupDn: "CN=Flare Admins,DC=corp,DC=example,DC=com",
            memberGroupDn: "CN=Flare Members,DC=corp,DC=example,DC=com",
            viewerGroupDn: "CN=Flare Viewers,DC=corp,DC=example,DC=com");
        var memberOf = new[]
        {
            "CN=Flare Viewers,DC=corp,DC=example,DC=com",
            "CN=Flare Admins,DC=corp,DC=example,DC=com",
        };

        Assert.Equal(UserRole.Admin, LdapAuthEndpoints.ResolveRole(memberOf, settings));
    }

    [Fact]
    public void ResolveRole_PicksMember_WhenNotInTheAdminGroup()
    {
        var settings = SettingsWithGroups(
            adminGroupDn: "CN=Flare Admins,DC=corp,DC=example,DC=com",
            memberGroupDn: "CN=Flare Members,DC=corp,DC=example,DC=com");
        var memberOf = new[] { "CN=Flare Members,DC=corp,DC=example,DC=com" };

        Assert.Equal(UserRole.Member, LdapAuthEndpoints.ResolveRole(memberOf, settings));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefaultRole_WhenNoGroupMatches()
    {
        var settings = SettingsWithGroups(adminGroupDn: "CN=Flare Admins,DC=corp,DC=example,DC=com", defaultRole: UserRole.Viewer);
        var memberOf = new[] { "CN=Some Other Group,DC=corp,DC=example,DC=com" };

        Assert.Equal(UserRole.Viewer, LdapAuthEndpoints.ResolveRole(memberOf, settings));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefaultRole_WhenNoGroupDnsAreConfiguredAtAll()
    {
        var settings = SettingsWithGroups(defaultRole: UserRole.Member);

        Assert.Equal(UserRole.Member, LdapAuthEndpoints.ResolveRole([], settings));
    }

    [Fact]
    public void ResolveRole_MatchesGroupDns_CaseInsensitively()
    {
        var settings = SettingsWithGroups(adminGroupDn: "CN=Flare Admins,DC=corp,DC=example,DC=com");
        var memberOf = new[] { "cn=flare admins,dc=corp,dc=example,dc=com" };

        Assert.Equal(UserRole.Admin, LdapAuthEndpoints.ResolveRole(memberOf, settings));
    }
}
