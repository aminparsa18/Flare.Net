using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;
using Flare.Identity.PasswordSetTokens;
using Flare.Identity.Users;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Login/logout/current-user/first-run-bootstrap, under <c>/api/auth</c>. The only
/// endpoint group left unauthenticated in <c>Program.cs</c> - by definition, a client
/// can't have a session yet when calling <c>/login</c> or the pre-login
/// <c>/bootstrap/status</c> check.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", HandleLoginAsync);
        endpoints.MapPost("/api/auth/logout", HandleLogoutAsync);
        endpoints.MapGet("/api/auth/me", HandleMeAsync);
        endpoints.MapPost("/api/auth/password", HandleChangePasswordAsync);
        endpoints.MapPost("/api/auth/set-password", HandleSetPasswordAsync);
        endpoints.MapPost("/api/auth/bootstrap", HandleBootstrapAsync);
        endpoints.MapGet("/api/auth/bootstrap/status", HandleBootstrapStatusAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleLoginAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IAuthSettingsStore authSettings,
        IOptions<AuthOptions> authOptions,
        ILoginAttemptStore loginAttempts,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Same disabled-gate convention EntraAuthEndpoints/the LDAP endpoints use - local
        // password login is its own toggle now (AuthSettings.LocalEnabled), independent
        // of the global AuthSettings.Enabled switch: bootstrapping/verifying a local
        // Admin account before flipping auth on globally is a reasonable workflow, so
        // this only checks LocalEnabled, not Enabled.
        if (!(await authSettings.GetAsync(cancellationToken)).LocalEnabled)
        {
            return Results.NotFound();
        }

        LoginRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AuthJsonContext.Default.LoginRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var clientIp = GetClientIp(http);
        var lockedUntil = await loginAttempts.GetLockedUntilAsync(request.Username, clientIp, cancellationToken);
        if (lockedUntil is { } locked)
        {
            // 429, not 401 - this pair is being throttled regardless of whether the
            // credentials about to be typed are even correct, so it's a distinct signal
            // from "wrong password". Doesn't leak whether the username exists: the same
            // (username, IP) pair had to fail here enough times to get locked out in the
            // first place, so nothing new is disclosed to whoever's attempting this now.
            http.Response.Headers["Retry-After"] = ((int)Math.Ceiling((locked - timeProvider.GetUtcNow()).TotalSeconds)).ToString();
            return Results.Problem("Too many failed login attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);
        }

        // VerifyPasswordAsync collapses "unknown username", "wrong password", and
        // "disabled account" into a single null result on purpose - the response here
        // must not distinguish them, to avoid leaking whether a username exists.
        var user = await users.VerifyPasswordAsync(request.Username, request.Password, cancellationToken);
        if (user is null)
        {
            await loginAttempts.RecordFailureAsync(request.Username, clientIp, cancellationToken);
            return Results.Unauthorized();
        }

        await loginAttempts.RecordSuccessAsync(request.Username, clientIp, cancellationToken);
        await SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return ApiSerialization.Write(http, ToDto(user), AuthJsonContext.Default.AuthUserDto);
    }

    /// <summary>The request's raw TCP peer, not a forwarded header - same deliberate
    /// choice as <see cref="Flare.Api.Auth.TrustedProxyNetworks"/> (see its remarks): trusting
    /// <c>X-Forwarded-For</c> here would let an attacker forge a fresh IP on every
    /// request and sidestep the lockout entirely. Behind a reverse proxy, every request
    /// therefore shares the proxy's own IP for throttling purposes - a known, accepted
    /// limitation of not trusting spoofable headers, not an oversight. Normalizes an
    /// IPv4-mapped-IPv6 peer (what Kestrel commonly reports behind Docker's default
    /// bridge network) to plain IPv4 so the same real client doesn't fragment across two
    /// different-looking keys depending on which representation shows up.</summary>
    private static string GetClientIp(HttpContext http)
    {
        var remoteIp = http.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return "unknown";
        }
        return (remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp).ToString();
    }

    internal static async Task<IResult> HandleLogoutAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IProxyAuthSettingsStore proxyAuthSettings,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        string? redirectUrl = null;
        if (http.Request.Cookies.TryGetValue(authOptions.Value.CookieName, out var token) && !string.IsNullOrEmpty(token))
        {
            // Looked up *before* deleting the session below - only to decide whether this
            // was a ReverseProxy-provisioned account, since that's the one auth method
            // whose logout can't fully happen client-side (see docs/auth.md's "Known
            // limitations" - Flare has no end-session endpoint to call for it, unlike
            // OIDC). Every other account keeps today's behavior: redirectUrl stays null,
            // the dashboard just returns to /login.
            var session = await sessions.FindAsync(token, cancellationToken);
            if (session is not null)
            {
                var user = await users.FindByIdAsync(session.UserId, cancellationToken);
                if (user is { AuthProvider: "ReverseProxy" })
                {
                    var proxySettings = await proxyAuthSettings.GetAsync(cancellationToken);
                    redirectUrl = string.IsNullOrWhiteSpace(proxySettings.LogoutRedirectUrl) ? null : proxySettings.LogoutRedirectUrl;
                }
            }

            await sessions.DeleteAsync(token, cancellationToken);
        }

        http.Response.Cookies.Delete(authOptions.Value.CookieName, new CookieOptions { Path = CookiePath(http) });
        return ApiSerialization.Write(http, new LogoutResponse { RedirectUrl = redirectUrl }, AuthJsonContext.Default.LogoutResponse);
    }

    internal static async Task<IResult> HandleMeAsync(HttpContext http, ClaimsPrincipal principal, IUserStore users, CancellationToken cancellationToken)
    {
        // AuthEndpoints isn't wrapped in RequireAuthorization() (it can't be - /login
        // itself has to be reachable pre-session), but Program.cs's UseAuthentication()
        // still runs for every request regardless of whether the endpoint requires it,
        // so `principal` is already populated from the session cookie here if one was
        // sent - this endpoint just has to check IsAuthenticated itself instead of
        // relying on the authorization middleware to 401 for it.
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return Results.Unauthorized();
        }

        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !Guid.TryParse(idClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        return user is null || user.IsDisabled ? Results.Unauthorized() : ApiSerialization.Write(http, ToDto(user), AuthJsonContext.Default.AuthUserDto);
    }

    internal const int MinPasswordLength = 8;

    /// <summary>Self-service change for a local account: verifies the current password, then
    /// revokes every other session (the caller's own stays signed in).</summary>
    internal static async Task<IResult> HandleChangePasswordAsync(
        HttpContext http,
        ClaimsPrincipal principal,
        IUserStore users,
        ISessionStore sessions,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        if (principal.Identity is not { IsAuthenticated: true }
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }

        ChangePasswordRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AuthJsonContext.Default.ChangePasswordRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            return Results.Problem("Current and new password are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.NewPassword.Length < MinPasswordLength)
        {
            return Results.Problem($"Password must be at least {MinPasswordLength} characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null || user.IsDisabled || user.AuthProvider != "Local")
        {
            return Results.Problem("Only local accounts have a Flare-managed password.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await users.VerifyPasswordAsync(user.Username, request.CurrentPassword, cancellationToken) is null)
        {
            return Results.Problem("Current password is incorrect.", statusCode: StatusCodes.Status400BadRequest);
        }

        await users.SetPasswordAsync(user.Id, request.NewPassword, cancellationToken);
        if (http.Request.Cookies.TryGetValue(authOptions.Value.CookieName, out var token) && !string.IsNullOrEmpty(token))
        {
            await sessions.DeleteAllForUserExceptAsync(user.Id, token, cancellationToken);
        }
        else
        {
            // Authenticated by PAT rather than cookie - no session to keep.
            await sessions.DeleteAllForUserAsync(user.Id, cancellationToken);
        }

        return Results.NoContent();
    }

    /// <summary>Redeems an invite/reset token (unauthenticated - the token is the credential),
    /// sets the password, and revokes all of the user's existing sessions. Does not sign in:
    /// the user logs in normally afterwards.</summary>
    internal static async Task<IResult> HandleSetPasswordAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IPasswordSetTokenStore tokens,
        CancellationToken cancellationToken)
    {
        SetPasswordRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AuthJsonContext.Default.SetPasswordRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem("Token and password are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Validated before consuming so a too-short password doesn't burn the link.
        if (request.Password.Length < MinPasswordLength)
        {
            return Results.Problem($"Password must be at least {MinPasswordLength} characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = await tokens.ConsumeAsync(request.Token, cancellationToken);
        var user = userId is { } id ? await users.FindByIdAsync(id, cancellationToken) : null;
        if (user is null || user.IsDisabled || user.AuthProvider != "Local")
        {
            return Results.Problem("This link is invalid or has expired.", statusCode: StatusCodes.Status400BadRequest);
        }

        await users.SetPasswordAsync(user.Id, request.Password, cancellationToken);
        await sessions.DeleteAllForUserAsync(user.Id, cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> HandleBootstrapAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IAuthSettingsStore authSettings,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        if (!(await authSettings.GetAsync(cancellationToken)).LocalEnabled)
        {
            return Results.NotFound();
        }

        // Benign, accepted race: two concurrent first-run requests could both observe
        // AnyAsync()==false and both proceed to CreateAsync. Worst case is two Admin
        // users instead of a clean 409 for the second - not worth a distributed lock for
        // a first-run-only edge case. A duplicate *username* still 409s via
        // Users.Username's UNIQUE constraint either way.
        if (await users.AnyAsync(cancellationToken))
        {
            return Results.Problem("An admin user already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        LoginRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AuthJsonContext.Default.LoginRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem("Username and password are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Password.Length < MinPasswordLength)
        {
            return Results.Problem($"Password must be at least {MinPasswordLength} characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        User user;
        try
        {
            user = await users.CreateAsync(request.Username, request.Password, UserRole.Admin, cancellationToken);
        }
        catch (Exception)
        {
            // The AnyAsync() race described above landed on the username-uniqueness
            // constraint - report it as the same 409 a duplicate-username create would be.
            // (Can't await inside a catch-when filter, so the check happens in the body.)
            if (await users.FindByUsernameAsync(request.Username, cancellationToken) is not null)
            {
                return Results.Problem("An admin user already exists.", statusCode: StatusCodes.Status409Conflict);
            }
            throw;
        }

        await SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return ApiSerialization.Write(http, ToDto(user), AuthJsonContext.Default.AuthUserDto, statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> HandleBootstrapStatusAsync(
        HttpContext http,
        IUserStore users,
        IEntraSettingsStore entraSettings,
        IAuthSettingsStore authSettings,
        ILdapSettingsStore ldapSettings,
        IOidcSettingsStore oidcSettings,
        IProxyAuthSettingsStore proxyAuthSettings,
        CancellationToken cancellationToken)
    {
        var needsBootstrap = !await users.AnyAsync(cancellationToken);
        var entra = await entraSettings.GetAsync(cancellationToken);
        var auth = await authSettings.GetAsync(cancellationToken);
        var ldap = await ldapSettings.GetAsync(cancellationToken);
        var oidc = await oidcSettings.GetAsync(cancellationToken);
        var proxyAuth = await proxyAuthSettings.GetAsync(cancellationToken);
        return ApiSerialization.Write(
            http,
            new BootstrapStatusResponse
            {
                NeedsBootstrap = needsBootstrap,
                EntraEnabled = entra.Enabled,
                AuthEnabled = auth.Enabled,
                LocalEnabled = auth.LocalEnabled,
                LdapEnabled = ldap.Enabled,
                OidcEnabled = oidc.Enabled,
                OidcDisplayName = oidc.DisplayName,
                ProxyAuthEnabled = proxyAuth.Enabled,
            },
            AuthJsonContext.Default.BootstrapStatusResponse);
    }

    /// <summary>Mints a <c>Sessions</c> row + <c>flare_session</c> cookie for
    /// <paramref name="user"/> - the one place either login path (password or Entra)
    /// ends up, so both produce byte-for-byte the same session/cookie shape. Internal
    /// (not private) so <see cref="EntraAuthEndpoints"/> can call it too.</summary>
    internal static async Task SignInAsync(HttpContext http, ISessionStore sessions, AuthOptions authOptions, User user, CancellationToken cancellationToken)
    {
        var session = await sessions.CreateAsync(user.Id, authOptions.SessionLifetime, cancellationToken);
        http.Response.Cookies.Append(authOptions.CookieName, session.Id, new CookieOptions
        {
            HttpOnly = true,
            Secure = authOptions.CookieSecure,
            SameSite = authOptions.CookieSameSite,
            Expires = session.ExpiresAt,
            Path = CookiePath(http),
        });
    }

    /// <summary>The session cookie's scope - the sub-path Flare is hosted under (<c>Flare:BasePath</c>), or <c>/</c> at the root.</summary>
    private static string CookiePath(HttpContext http) => http.Request.PathBase.HasValue ? http.Request.PathBase.Value! : "/";

    internal static AuthUserDto ToDto(User user) => new() { Id = user.Id, Username = user.Username, Role = user.Role, AuthProvider = user.AuthProvider };
}
