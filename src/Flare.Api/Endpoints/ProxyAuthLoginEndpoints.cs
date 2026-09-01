using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Reverse-proxy / trusted-header login, under <c>/api/auth/proxy</c> - unauthenticated
/// by design, same reasoning as <see cref="AuthEndpoints"/>. Shaped like
/// <see cref="LdapAuthEndpoints"/> (a single POST/JSON endpoint, no ASP.NET Core
/// authentication *scheme*, settings read fresh on every attempt so no restart is
/// needed), not like <see cref="EntraAuthEndpoints"/>/<c>OidcAuthEndpoints</c>'s
/// redirect dance - there's no external provider to redirect to, identity is already
/// established by the time the request reaches this endpoint (see docs/auth.md's
/// "Reverse proxy (trusted header)" section for the full picture).
///
/// Called automatically by the dashboard's <c>/login</c> page (no request body, no user
/// action) whenever <c>GET /api/auth/bootstrap/status</c> reports
/// <c>proxyAuthEnabled: true</c> - unlike every other method, there's no "Sign in with
/// ..." button, since there's nothing for a human to click.
/// </summary>
public static class ProxyAuthLoginEndpoints
{
    public static IEndpointRouteBuilder MapProxyAuthLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/proxy/login", HandleLoginAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleLoginAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        IProxyAuthSettingsStore proxySettings,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        var settings = await proxySettings.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Results.NotFound();
        }

        // The entire security boundary for this method - see TrustedProxyNetworks'
        // remarks for why this checks the request's own TCP peer, not a forwarded
        // header. A distinct 403 (not the header-missing 401 below) so an Admin
        // debugging a misconfigured allowlist isn't misled into thinking the proxy
        // itself is broken.
        if (!TrustedProxyNetworks.IsTrusted(http.Connection.RemoteIpAddress, settings.TrustedProxyCidrs))
        {
            return Results.Problem("Caller is not in a trusted network.", statusCode: StatusCodes.Status403Forbidden);
        }

        var externalId = http.Request.Headers[settings.HeaderName].ToString().Trim();
        if (externalId.Length == 0)
        {
            // The proxy didn't actually authenticate this caller (or Flare is being hit
            // directly, bypassing it, from an address that happens to be trusted) - same
            // "no usable credential" shape a missing/blank field gets everywhere else.
            return Results.Unauthorized();
        }

        var user = await users.FindByExternalIdAsync("ReverseProxy", externalId, cancellationToken);
        if (user is null)
        {
            var groupsHeaderValue = string.IsNullOrEmpty(settings.GroupsHeaderName) ? null : http.Request.Headers[settings.GroupsHeaderName].ToString();
            var role = ResolveRole(groupsHeaderValue, settings);
            // The header value is both the stable ExternalId and the seed username -
            // unlike Entra/OIDC there's no separate identifier claim vs. display name to
            // prefer between.
            user = await users.CreateFromExternalAsync("ReverseProxy", externalId, externalId, role, cancellationToken);
        }

        if (user.IsDisabled)
        {
            // Same generic-401-for-a-disabled-account convention LdapAuthEndpoints uses.
            return Results.Unauthorized();
        }

        await AuthEndpoints.SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return ApiSerialization.Write(http, AuthEndpoints.ToDto(user), AuthJsonContext.Default.AuthUserDto);
    }

    /// <summary>Highest-privilege matching group name (Admin > Member > Viewer), or
    /// <see cref="ProxyAuthSettings.DefaultRole"/> if <see cref="ProxyAuthSettings.GroupsHeaderName"/>
    /// isn't configured, the header is absent on this request, or it matches none of the
    /// three configured group names. Mirrors <see cref="LdapAuthEndpoints.ResolveRole"/>'s
    /// same precedence over AD group DNs, just comma-separated header values instead of
    /// <c>memberOf</c> - comparison is ordinal-case-insensitive for the same reason LDAP's
    /// is: group naming conventions vary in casing across proxies/IdPs.</summary>
    internal static UserRole ResolveRole(string? groupsHeaderValue, ProxyAuthSettings settings)
    {
        if (string.IsNullOrEmpty(groupsHeaderValue))
        {
            return settings.DefaultRole;
        }

        var groups = groupsHeaderValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool IsMember(string? group) => group is not null && groups.Contains(group, StringComparer.OrdinalIgnoreCase);

        if (IsMember(settings.AdminGroup))
        {
            return UserRole.Admin;
        }
        if (IsMember(settings.MemberGroup))
        {
            return UserRole.Member;
        }
        if (IsMember(settings.ViewerGroup))
        {
            return UserRole.Viewer;
        }
        return settings.DefaultRole;
    }
}
