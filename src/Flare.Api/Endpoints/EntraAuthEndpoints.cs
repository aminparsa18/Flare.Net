using System.Security.Claims;
using Flare.Api.Auth;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Microsoft Entra ID (SSO) login, under <c>/api/auth/entra</c> - unauthenticated by
/// design, same reasoning as <see cref="AuthEndpoints"/>. Two endpoints implement the
/// whole flow (see docs/auth.md's "Microsoft Entra ID (SSO)" section for the full
/// picture):
///
/// 1. <c>GET /api/auth/entra/login</c> challenges the <c>Entra</c> OpenIdConnect scheme,
///    redirecting the browser to Microsoft.
/// 2. Microsoft redirects back to the OIDC handler's own callback path
///    (<c>/signin-oidc</c>, ASP.NET Core's default - handled entirely by framework
///    middleware, never mapped here) which validates the token and signs the resulting
///    principal into the short-lived <see cref="EntraAuthenticationDefaults.ExternalCookieScheme"/>
///    cookie, then redirects to <c>GET /api/auth/entra/complete</c> (the
///    <c>RedirectUri</c> set on the challenge below) - this is the standard ASP.NET Core
///    "paired external cookie" pattern for doing custom post-login work outside the
///    remote-auth handler's own request pipeline, deliberately not attempted inside an
///    <c>OnTicketReceived</c> event.
/// 3. <c>complete</c> reads that principal, provisions/looks up the local
///    <see cref="Identity.Users.User"/> row, mints the real <c>flare_session</c> cookie
///    via <see cref="AuthEndpoints.SignInAsync"/>, signs the external cookie back out
///    (single use, never the app's real session), and redirects to the dashboard.
/// </summary>
public static class EntraAuthEndpoints
{
    public static IEndpointRouteBuilder MapEntraAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/entra/login", HandleLoginAsync);
        endpoints.MapGet("/api/auth/entra/complete", HandleCompleteAsync);
        return endpoints;
    }

    internal static IResult HandleLoginAsync(string? returnUrl, IConfiguration configuration, IOptions<EntraOptions> entraOptions)
    {
        if (!entraOptions.Value.Enabled)
        {
            return Results.NotFound();
        }

        // Same allow-list CORS already enforces for the dashboard's own origin
        // (Program.cs) - reused here as an open-redirect guard, since this endpoint's
        // whole job ends in a Results.Redirect to whatever returnUrl says.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var validatedReturnUrl = ValidateReturnUrl(returnUrl, allowedOrigins);
        if (validatedReturnUrl is null)
        {
            return Results.Problem("returnUrl is missing or is not an allowed origin.", statusCode: StatusCodes.Status400BadRequest);
        }

        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/entra/complete" };
        properties.Items["returnUrl"] = validatedReturnUrl;
        return Results.Challenge(properties, [EntraAuthenticationDefaults.SchemeName]);
    }

    internal static async Task<IResult> HandleCompleteAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IOptions<AuthOptions> authOptions,
        IOptions<EntraOptions> entraOptions,
        CancellationToken cancellationToken)
    {
        // Defensive: the "EntraExternal"/"Entra" schemes are only registered in
        // Program.cs when Auth:Entra:Enabled is true (see its remarks), so this guards
        // against an InvalidOperationException from AuthenticateAsync if this endpoint
        // were ever hit directly with Entra disabled - unreachable via the normal login
        // flow (HandleLoginAsync already 404s first), but not via a hand-crafted request.
        if (!entraOptions.Value.Enabled)
        {
            return Results.NotFound();
        }

        var external = await http.AuthenticateAsync(EntraAuthenticationDefaults.ExternalCookieScheme);
        if (!external.Succeeded || external.Principal is null)
        {
            return Results.Problem("Entra sign-in did not complete.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var returnUrl = external.Properties?.Items.TryGetValue("returnUrl", out var storedReturnUrl) == true ? storedReturnUrl : null;

        var externalId = GetExternalId(external.Principal);
        if (externalId is null)
        {
            return Results.Problem("Entra token did not contain an object id (oid) claim.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await users.FindByExternalIdAsync("Entra", externalId, cancellationToken);
        if (user is null)
        {
            var username = GetUsername(external.Principal);
            var role = ResolveRole(external.Principal, entraOptions.Value.DefaultRole);
            user = await users.CreateFromExternalAsync("Entra", externalId, username, role, cancellationToken);
        }

        // Single-use handoff cookie - done with it either way, success or reject below.
        await http.SignOutAsync(EntraAuthenticationDefaults.ExternalCookieScheme);

        if (user.IsDisabled)
        {
            return Results.Redirect(AppendQuery(returnUrl, "/login", "error", "account-disabled"));
        }

        await AuthEndpoints.SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return Results.Redirect(returnUrl ?? "/");
    }

    /// <summary>The stable Entra identifier for the signed-in account - "oid" (object
    /// id), Microsoft's documented recommendation for a durable per-user identifier,
    /// falling back to the standard "sub" mapping for any token that omits it.</summary>
    internal static string? GetExternalId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("oid") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Best-effort human-readable username for a first-time provision - never
    /// re-read afterward, so any of these claims being present is enough.</summary>
    internal static string GetUsername(ClaimsPrincipal principal) =>
        principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? principal.FindFirstValue("name")
        ?? $"entra-user-{Guid.NewGuid():N}";

    /// <summary>Highest-privilege Entra App Role assigned to this account, or
    /// <paramref name="defaultRole"/> if the token carries no <c>roles</c> claim
    /// recognized as one of Flare's three role names (no App Role assigned in Entra, or
    /// App Roles not configured on the App Registration at all).</summary>
    internal static UserRole ResolveRole(ClaimsPrincipal principal, UserRole defaultRole)
    {
        var roleClaims = new HashSet<string>(principal.FindAll("roles").Select(c => c.Value), StringComparer.OrdinalIgnoreCase);
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

    internal static string? ValidateReturnUrl(string? returnUrl, IReadOnlyCollection<string> allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var origin = $"{uri.Scheme}://{uri.Authority}";
        return allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) ? returnUrl : null;
    }

    private static string AppendQuery(string? origin, string path, string key, string value)
    {
        var baseUrl = string.IsNullOrEmpty(origin) ? path : $"{origin}{path}";
        return $"{baseUrl}?{key}={Uri.EscapeDataString(value)}";
    }
}
