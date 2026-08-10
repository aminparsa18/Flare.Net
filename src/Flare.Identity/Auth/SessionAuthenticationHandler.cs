using System.Security.Claims;
using System.Text.Encodings.Web;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flare.Identity.Auth;

/// <summary>
/// Resolves the <c>flare_session</c> cookie into a <see cref="ClaimsPrincipal"/> via a
/// <see cref="Session"/>/<see cref="User"/> lookup. A custom
/// <see cref="AuthenticationHandler{TOptions}"/> - the same extensibility point ASP.NET
/// Core's own Cookie Authentication middleware is built on, used here one layer lower so
/// this can read the session store directly rather than encoding claims into a
/// self-contained cookie payload. This is also what makes the WebSocket live-tail
/// endpoint (<c>LogTailEndpoints</c>) work with a plain <c>RequireAuthorization()</c>
/// with no special-casing: the browser sends the cookie automatically on the WS upgrade
/// request, and this handler runs like it would for any other request.
/// </summary>
/// <remarks>
/// Registered once as the default scheme in <c>Flare.Api/Program.cs</c>
/// (<see cref="SessionAuthenticationDefaults.SchemeName"/>). A future OIDC/Entra ID
/// scheme (docs/auth.md's pluggability seam) is added as a second, independent
/// <c>AddOpenIdConnect()</c> registration alongside this one - nothing here needs to
/// change for that.
/// </remarks>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<SessionAuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISessionStore sessionStore,
    IUserStore userStore,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<SessionAuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    // Only bump Sessions.LastSeenAt this often per session - it's for an admin-facing
    // "last active" display, not for expiry, so writing it on every single request would
    // be pure overhead for no behavioral benefit.
    private static readonly TimeSpan LastSeenTouchInterval = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
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

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
