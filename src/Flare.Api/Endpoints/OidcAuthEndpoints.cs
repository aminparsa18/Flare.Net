using System.Security.Claims;
using Flare.Api.Auth;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Generic OpenID Connect login, under <c>/api/auth/oidc</c> - unauthenticated by design,
/// same reasoning as <see cref="AuthEndpoints"/>. Structurally identical to
/// <see cref="EntraAuthEndpoints"/> (see docs/auth.md's "OpenID Connect" section for the
/// full picture) - two endpoints implement the whole flow:
///
/// 1. <c>GET /api/auth/oidc/login</c> challenges the <c>Oidc</c> OpenIdConnect scheme,
///    redirecting the browser to the configured provider.
/// 2. The provider redirects back to the OIDC handler's own callback path
///    (<see cref="OidcAuthenticationDefaults.CallbackPath"/>, handled entirely by
///    framework middleware, never mapped here) which validates the token and signs the
///    resulting principal into the short-lived
///    <see cref="OidcAuthenticationDefaults.ExternalCookieScheme"/> cookie, then
///    redirects to <c>GET /api/auth/oidc/complete</c> (the <c>RedirectUri</c> set on the
///    challenge below) - same "paired external cookie" pattern Entra ID uses.
/// 3. <c>complete</c> reads that principal, provisions/looks up the local
///    <see cref="Identity.Users.User"/> row, mints the real <c>flare_session</c> cookie
///    via <see cref="AuthEndpoints.SignInAsync"/>, signs the external cookie back out,
///    and redirects to the dashboard.
/// </summary>
public static class OidcAuthEndpoints
{
    public static IEndpointRouteBuilder MapOidcAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/oidc/login", HandleLoginAsync);
        endpoints.MapGet("/api/auth/oidc/complete", HandleCompleteAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleLoginAsync(string? returnUrl, IConfiguration configuration, IOidcSettingsStore oidcSettings, CancellationToken cancellationToken)
    {
        var settings = await oidcSettings.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Results.NotFound();
        }

        // Same allow-list CORS already enforces for the dashboard's own origin
        // (Program.cs) - reused here as an open-redirect guard, same as
        // EntraAuthEndpoints.HandleLoginAsync.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var validatedReturnUrl = EntraAuthEndpoints.ValidateReturnUrl(returnUrl, allowedOrigins);
        if (validatedReturnUrl is null)
        {
            return Results.Problem("returnUrl is missing or is not an allowed origin.", statusCode: StatusCodes.Status400BadRequest);
        }

        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/oidc/complete" };
        properties.Items["returnUrl"] = validatedReturnUrl;
        return Results.Challenge(properties, [OidcAuthenticationDefaults.SchemeName]);
    }

    internal static async Task<IResult> HandleCompleteAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IOidcSettingsStore oidcSettings,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        // Defensive: the "OidcExternal"/"Oidc" auth *schemes* are always registered
        // (Program.cs), but the login flow itself is still gated on Enabled - guards the
        // same disabled case HandleLoginAsync already 404s on, for a hand-crafted request
        // that skips straight to this endpoint.
        var settings = await oidcSettings.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Results.NotFound();
        }

        var external = await http.AuthenticateAsync(OidcAuthenticationDefaults.ExternalCookieScheme);
        if (!external.Succeeded || external.Principal is null)
        {
            return Results.Problem("OpenID Connect sign-in did not complete.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var returnUrl = external.Properties?.Items.TryGetValue("returnUrl", out var storedReturnUrl) == true ? storedReturnUrl : null;

        var externalId = GetExternalId(external.Principal);
        if (externalId is null)
        {
            return Results.Problem("OpenID Connect token did not contain a sub claim.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await users.FindByExternalIdAsync("Oidc", externalId, cancellationToken);
        if (user is null)
        {
            var username = GetUsername(external.Principal);
            var role = ResolveRole(external.Principal, settings.RoleClaimName, settings.DefaultRole);
            user = await users.CreateFromExternalAsync("Oidc", externalId, username, role, cancellationToken);
        }

        // Single-use handoff cookie - done with it either way, success or reject below.
        await http.SignOutAsync(OidcAuthenticationDefaults.ExternalCookieScheme);

        if (user.IsDisabled)
        {
            return Results.Redirect(AppendQuery(returnUrl, "/login", "error", "account-disabled"));
        }

        await AuthEndpoints.SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return Results.Redirect(returnUrl ?? "/");
    }

    /// <summary>The stable OIDC identifier for the signed-in account - <c>sub</c>, the
    /// standard, provider-agnostic claim every conformant OIDC token carries (unlike
    /// Entra's Microsoft-specific <c>oid</c> preference), falling back to the standard
    /// <see cref="ClaimTypes.NameIdentifier"/> mapping for any token that maps it
    /// differently.</summary>
    internal static string? GetExternalId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Best-effort human-readable username for a first-time provision - never
    /// re-read afterward, so any of these claims being present is enough. Same fallback
    /// chain as <see cref="EntraAuthEndpoints.GetUsername"/>.</summary>
    internal static string GetUsername(ClaimsPrincipal principal) =>
        principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? principal.FindFirstValue("name")
        ?? $"oidc-user-{Guid.NewGuid():N}";

    /// <summary>Highest-privilege value found under <paramref name="roleClaimName"/> (the
    /// dashboard-configured claim name, unlike Entra's hardcoded <c>"roles"</c>), or
    /// <paramref name="defaultRole"/> if that claim is absent or carries no recognized
    /// value.</summary>
    internal static UserRole ResolveRole(ClaimsPrincipal principal, string roleClaimName, UserRole defaultRole)
    {
        var roleClaims = new HashSet<string>(principal.FindAll(roleClaimName).Select(c => c.Value), StringComparer.OrdinalIgnoreCase);
        if (roleClaims.Contains(nameof(UserRole.Admin)))
        {
            return UserRole.Admin;
        }
        if (roleClaims.Contains(nameof(UserRole.Member)))
        {
            return UserRole.Member;
        }
        if (roleClaims.Contains(nameof(UserRole.Viewer)))
        {
            return UserRole.Viewer;
        }
        return defaultRole;
    }

    private static string AppendQuery(string? origin, string path, string key, string value)
    {
        var baseUrl = string.IsNullOrEmpty(origin) ? path : $"{origin}{path}";
        return $"{baseUrl}?{key}={Uri.EscapeDataString(value)}";
    }
}
