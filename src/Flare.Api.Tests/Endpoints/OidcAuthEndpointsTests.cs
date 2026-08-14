using System.Security.Claims;
using Flare.Api.Endpoints;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="OidcAuthEndpoints"/>' pure claim-extraction/role-resolution logic
/// directly - same convention/scope as <see cref="EntraAuthEndpointsTests"/> (reusing
/// <see cref="EntraAuthEndpoints.ValidateReturnUrl"/>'s own coverage there rather than
/// duplicating it, since <see cref="OidcAuthEndpoints"/> calls that same method).
/// <c>HandleCompleteAsync</c> itself isn't unit-tested here for the same reason Entra's
/// isn't - it needs a real authenticated "OidcExternal" scheme principal on the
/// HttpContext, which only a live OIDC round-trip produces.
/// </summary>
public class OidcAuthEndpointsTests
{
    [Fact]
    public void ResolveRole_PicksAdmin_WhenMultipleRoleClaimsPresent()
    {
        var principal = PrincipalWithRoles("roles", "Viewer", "Admin", "Member");

        Assert.Equal(UserRole.Admin, OidcAuthEndpoints.ResolveRole(principal, "roles", UserRole.Viewer));
    }

    [Fact]
    public void ResolveRole_PicksMember_WhenNoAdminClaimPresent()
    {
        var principal = PrincipalWithRoles("roles", "Viewer", "Member");

        Assert.Equal(UserRole.Member, OidcAuthEndpoints.ResolveRole(principal, "roles", UserRole.Viewer));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenNoRecognizedRoleClaimPresent()
    {
        var principal = PrincipalWithRoles("roles", "SomeOtherGroup");

        Assert.Equal(UserRole.Viewer, OidcAuthEndpoints.ResolveRole(principal, "roles", UserRole.Viewer));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenNoRoleClaimAtAll()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(UserRole.Member, OidcAuthEndpoints.ResolveRole(principal, "roles", UserRole.Member));
    }

    [Fact]
    public void ResolveRole_UsesTheConfiguredClaimName_NotAFixedOne()
    {
        // Unlike Entra's hardcoded "roles" claim, the dashboard-configured claim name is
        // whatever this provider actually issues - "groups" here, deliberately not "roles".
        var principal = PrincipalWithRoles("groups", "Admin");

        Assert.Equal(UserRole.Admin, OidcAuthEndpoints.ResolveRole(principal, "groups", UserRole.Viewer));
        Assert.Equal(UserRole.Viewer, OidcAuthEndpoints.ResolveRole(principal, "roles", UserRole.Viewer));
    }

    [Fact]
    public void GetExternalId_PrefersSubClaim()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "sub-value"), new Claim(ClaimTypes.NameIdentifier, "other-value")]);

        Assert.Equal("sub-value", OidcAuthEndpoints.GetExternalId(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void GetExternalId_FallsBackToNameIdentifier_WhenNoSubClaim()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sub-value")]);

        Assert.Equal("sub-value", OidcAuthEndpoints.GetExternalId(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void GetUsername_PrefersPreferredUsernameClaim()
    {
        var identity = new ClaimsIdentity([new Claim("preferred_username", "alice@example.com"), new Claim(ClaimTypes.Email, "other@example.com")]);

        Assert.Equal("alice@example.com", OidcAuthEndpoints.GetUsername(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void GetUsername_FallsBackThroughEmailThenName()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, "bob@example.com")]);

        Assert.Equal("bob@example.com", OidcAuthEndpoints.GetUsername(new ClaimsPrincipal(identity)));
    }

    // HandleLoginAsync/HandleCompleteAsync themselves aren't unit-tested here - same
    // reasoning EntraAuthEndpointsTests documents: HandleLoginAsync's only non-trivial
    // logic is the shared EntraAuthEndpoints.ValidateReturnUrl (already fully covered by
    // EntraAuthEndpointsTests), and HandleCompleteAsync needs a live "OidcExternal"-
    // authenticated principal, which only a real OIDC round-trip produces (see
    // docs/auth.md's manual verification steps).

    private static ClaimsPrincipal PrincipalWithRoles(string claimName, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(claimName, r));
        return new ClaimsPrincipal(new ClaimsIdentity(claims));
    }
}
