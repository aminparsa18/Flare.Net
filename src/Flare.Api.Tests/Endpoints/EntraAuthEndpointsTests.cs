using System.Security.Claims;
using Flare.Api.Endpoints;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="EntraAuthEndpoints"/>' pure claim-extraction/role-resolution/
/// return-url-validation logic directly - the parts of the Entra flow that don't need a
/// live IdP round-trip to verify (see docs/auth.md's "Microsoft Entra ID (SSO)" section
/// for what's left to verify against a real tenant). <c>HandleCompleteAsync</c> itself
/// isn't unit-tested here - it needs a real authenticated "EntraExternal" scheme
/// principal on the HttpContext, which only a live OIDC round-trip (or a much heavier
/// WebApplicationFactory-based test) produces.
/// </summary>
public class EntraAuthEndpointsTests
{
    [Fact]
    public void ResolveRole_PicksAdmin_WhenMultipleRoleClaimsPresent()
    {
        var principal = PrincipalWithRoles("Viewer", "Admin", "Member");

        Assert.Equal(UserRole.Admin, EntraAuthEndpoints.ResolveRole(principal, UserRole.Viewer));
    }

    [Fact]
    public void ResolveRole_PicksMember_WhenNoAdminClaimPresent()
    {
        var principal = PrincipalWithRoles("Viewer", "Member");

        Assert.Equal(UserRole.Member, EntraAuthEndpoints.ResolveRole(principal, UserRole.Viewer));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenNoRecognizedRoleClaimPresent()
    {
        var principal = PrincipalWithRoles("SomeOtherAppRole");

        Assert.Equal(UserRole.Viewer, EntraAuthEndpoints.ResolveRole(principal, UserRole.Viewer));
    }

    [Fact]
    public void ResolveRole_FallsBackToDefault_WhenNoRolesClaimAtAll()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(UserRole.Member, EntraAuthEndpoints.ResolveRole(principal, UserRole.Member));
    }

    [Fact]
    public void GetExternalId_PrefersOidClaim()
    {
        var identity = new ClaimsIdentity([new Claim("oid", "oid-value"), new Claim(ClaimTypes.NameIdentifier, "sub-value")]);

        Assert.Equal("oid-value", EntraAuthEndpoints.GetExternalId(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void GetExternalId_FallsBackToNameIdentifier_WhenNoOidClaim()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sub-value")]);

        Assert.Equal("sub-value", EntraAuthEndpoints.GetExternalId(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void GetUsername_PrefersPreferredUsernameClaim()
    {
        var identity = new ClaimsIdentity([new Claim("preferred_username", "alice@example.com"), new Claim(ClaimTypes.Email, "other@example.com")]);

        Assert.Equal("alice@example.com", EntraAuthEndpoints.GetUsername(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void GetUsername_FallsBackThroughEmailThenName()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, "bob@example.com")]);

        Assert.Equal("bob@example.com", EntraAuthEndpoints.GetUsername(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void ValidateReturnUrl_ReturnsTheUrl_WhenItsOriginIsAllowed()
    {
        var result = EntraAuthEndpoints.ValidateReturnUrl("http://localhost:3000", ["http://localhost:3000"]);

        Assert.Equal("http://localhost:3000", result);
    }

    [Fact]
    public void ValidateReturnUrl_ReturnsNull_ForAnOriginNotOnTheAllowList()
    {
        Assert.Null(EntraAuthEndpoints.ValidateReturnUrl("http://evil.example", ["http://localhost:3000"]));
    }

    [Fact]
    public void ValidateReturnUrl_ReturnsNull_ForAMalformedUrl()
    {
        Assert.Null(EntraAuthEndpoints.ValidateReturnUrl("not-a-url", ["http://localhost:3000"]));
    }

    [Fact]
    public void ValidateReturnUrl_ReturnsNull_ForNullOrEmpty()
    {
        Assert.Null(EntraAuthEndpoints.ValidateReturnUrl(null, ["http://localhost:3000"]));
        Assert.Null(EntraAuthEndpoints.ValidateReturnUrl("", ["http://localhost:3000"]));
    }

    // HandleLoginAsync/HandleCompleteAsync themselves aren't unit-tested here -
    // HandleLoginAsync's only non-trivial logic is ValidateReturnUrl (fully covered
    // above), and driving it through a real IConfiguration/HttpContext would need a
    // WebApplicationFactory-weight test for marginal extra coverage; HandleCompleteAsync
    // needs a live "EntraExternal"-authenticated principal, which only a real OIDC
    // round-trip produces (see docs/auth.md's manual verification steps).

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = roles.Select(r => new Claim("roles", r));
        return new ClaimsPrincipal(new ClaimsIdentity(claims));
    }
}
