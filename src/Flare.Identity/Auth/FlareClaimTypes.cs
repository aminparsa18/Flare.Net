namespace Flare.Identity.Auth;

/// <summary>Claim type constants beyond the standard <see cref="System.Security.Claims.ClaimTypes"/>
/// set, shared between <see cref="SessionAuthenticationHandler"/> (which adds them) and
/// whatever downstream code needs to read them back off <c>HttpContext.User</c>.</summary>
public static class FlareClaimTypes
{
    /// <summary>Present only on a ticket built from a personal access token (never a
    /// cookie session) - its value is the authenticating <c>PersonalAccessToken.Id</c>.
    /// Lets downstream code (e.g. <c>Flare.Api/Program.cs</c>'s PAT rate-limit policy)
    /// tell a PAT-authenticated request apart from a session one without a second
    /// authentication scheme.</summary>
    public const string PersonalAccessTokenId = "flare:pat_id";
}
