using System.Security.Claims;
using System.Text.Encodings.Web;
using Flare.Identity.PersonalAccessTokens;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flare.Identity.Auth;

/// <summary>
/// Resolves either the <c>flare_session</c> cookie or an <c>Authorization: Bearer
/// flr_pat_...</c> personal access token into a <see cref="ClaimsPrincipal"/> via a
/// <see cref="Session"/>/<see cref="PersonalAccessToken"/> + <see cref="User"/> lookup. A
/// custom <see cref="AuthenticationHandler{TOptions}"/> - the same extensibility point
/// ASP.NET Core's own Cookie Authentication middleware is built on, used here one layer
/// lower so this can read the session/token stores directly rather than encoding claims
/// into a self-contained payload. This is also what makes the WebSocket live-tail
/// endpoint (<c>LogTailEndpoints</c>) work with a plain <c>RequireAuthorization()</c>
/// with no special-casing: the browser sends the cookie automatically on the WS upgrade
/// request, and this handler runs like it would for any other request. (A PAT, being a
/// header rather than a cookie, does NOT work against the WebSocket upgrade the way the
/// cookie does - a browser can't attach a custom header to that handshake. PATs are for
/// plain HTTP request/response calls; see docs-internal/adr/0019-personal-access-tokens.md.)
/// </summary>
/// <remarks>
/// Registered once as the default scheme in <c>Flare.Api/Program.cs</c>
/// (<see cref="SessionAuthenticationDefaults.SchemeName"/>). A future OIDC/Entra ID
/// scheme (docs/auth.md's pluggability seam) is added as a second, independent
/// <c>AddOpenIdConnect()</c> registration alongside this one - nothing here needs to
/// change for that. Personal access tokens deliberately did NOT get their own scheme the
/// same way - see the ADR above for why folding bearer-token resolution into this single
/// scheme (rather than a second `AddScheme` + a policy scheme to pick between them) keeps
/// every existing `RequireAuthorization()`/`RequireMember`/`RequireAdmin` call working
/// unmodified for either credential type.
/// </remarks>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<SessionAuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISessionStore sessionStore,
    IUserStore userStore,
    IPersonalAccessTokenStore personalAccessTokenStore,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<SessionAuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    // Only bump Sessions.LastSeenAt/PersonalAccessTokens.LastUsedAt this often per
    // session/token - it's for an admin/self-facing "last active"/"last used" display,
    // not for expiry, so writing it on every single request would be pure overhead for
    // no behavioral benefit.
    private static readonly TimeSpan LastSeenTouchInterval = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (TryGetBearerToken(out var bearerToken))
        {
            return await AuthenticatePersonalAccessTokenAsync(bearerToken);
        }

        if (!Request.Cookies.TryGetValue(authOptions.Value.CookieName, out var token) || string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await sessionStore.FindAsync(token, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("Session not found or expired.");
        }

        var user = await userStore.FindByIdAsync(session.UserId, Context.RequestAborted);
        if (user is null || user.IsDisabled)
        {
            return AuthenticateResult.Fail("User not found or disabled.");
        }

        if (DateTimeOffset.UtcNow - session.LastSeenAt > LastSeenTouchInterval)
        {
            await sessionStore.TouchLastSeenAsync(token, Context.RequestAborted);
        }

        return AuthenticateResult.Success(BuildTicket(user));
    }

    /// <summary>Only a token that looks like ours (<see cref="PersonalAccessTokenHasher.Prefix"/>)
    /// is treated as a PAT attempt - anything else in the <c>Authorization</c> header
    /// (there currently is nothing else expected here, but this stays narrow rather than
    /// eating every malformed bearer value as a failed PAT lookup) falls through to
    /// <see cref="AuthenticateResult.NoResult"/> via the cookie path below finding no
    /// cookie either.</summary>
    private bool TryGetBearerToken(out string bearerToken)
    {
        bearerToken = "";
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = header["Bearer ".Length..].Trim();
        if (!candidate.StartsWith(PersonalAccessTokenHasher.Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        bearerToken = candidate;
        return true;
    }

    private async Task<AuthenticateResult> AuthenticatePersonalAccessTokenAsync(string rawToken)
    {
        var pat = await personalAccessTokenStore.ValidateAsync(rawToken, Context.RequestAborted);
        if (pat is null)
        {
            return AuthenticateResult.Fail("Personal access token not found, revoked, or expired.");
        }

        var user = await userStore.FindByIdAsync(pat.UserId, Context.RequestAborted);
        if (user is null || user.IsDisabled)
        {
            return AuthenticateResult.Fail("User not found or disabled.");
        }

        if (pat.LastUsedAt is null || DateTimeOffset.UtcNow - pat.LastUsedAt > LastSeenTouchInterval)
        {
            await personalAccessTokenStore.TouchLastUsedAsync(pat.Id, Context.RequestAborted);
        }

        return AuthenticateResult.Success(BuildTicket(user));
    }

    private AuthenticationTicket BuildTicket(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}
