using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;
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
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AuthJsonContext.Default.LoginRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        // VerifyPasswordAsync collapses "unknown username", "wrong password", and
        // "disabled account" into a single null result on purpose - the response here
        // must not distinguish them, to avoid leaking whether a username exists.
        var user = await users.VerifyPasswordAsync(request.Username, request.Password, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        await SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return Results.Json(ToDto(user), AuthJsonContext.Default.AuthUserDto);
    }

    internal static async Task<IResult> HandleLogoutAsync(
        HttpContext http,
        ISessionStore sessions,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        if (http.Request.Cookies.TryGetValue(authOptions.Value.CookieName, out var token) && !string.IsNullOrEmpty(token))
        {
            await sessions.DeleteAsync(token, cancellationToken);
        }

        http.Response.Cookies.Delete(authOptions.Value.CookieName, new CookieOptions { Path = "/" });
        return Results.NoContent();
    }

    internal static async Task<IResult> HandleMeAsync(ClaimsPrincipal principal, IUserStore users, CancellationToken cancellationToken)
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
        return user is null || user.IsDisabled ? Results.Unauthorized() : Results.Json(ToDto(user), AuthJsonContext.Default.AuthUserDto);
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
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AuthJsonContext.Default.LoginRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem("Username and password are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Password.Length < 8)
        {
            return Results.Problem("Password must be at least 8 characters.", statusCode: StatusCodes.Status400BadRequest);
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
        return Results.Json(ToDto(user), AuthJsonContext.Default.AuthUserDto, statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> HandleBootstrapStatusAsync(
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
        return Results.Json(
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
            Path = "/",
        });
    }

    internal static AuthUserDto ToDto(User user) => new() { Id = user.Id, Username = user.Username, Role = user.Role, AuthProvider = user.AuthProvider };
}
