using Flare.Identity.Users;

namespace Flare.Api.Model;

/// <summary>Request body for <c>POST /api/auth/login</c> and (reusing the same shape)
/// <c>POST /api/auth/bootstrap</c>.</summary>
public sealed record LoginRequest
{
    public required string Username { get; init; }

    public required string Password { get; init; }
}

/// <summary>The authenticated-user shape returned by login/bootstrap/<c>/me</c>.
/// Deliberately never carries a password hash or session token - see
/// <see cref="Identity.Users.User"/>'s remarks.</summary>
public sealed record AuthUserDto
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required UserRole Role { get; init; }

    /// <summary>"Local", "Entra", "ActiveDirectory", "Oidc", or "ReverseProxy" - lets the
    /// dashboard show e.g. a disabled password field for an SSO account without a second
    /// round-trip.</summary>
    public required string AuthProvider { get; init; }
}

/// <summary>Response body for <c>POST /api/auth/logout</c>. <see cref="RedirectUrl"/> is
/// non-null only for a <c>ReverseProxy</c>-provisioned account with a configured
/// <c>ProxyAuthSettings.LogoutRedirectUrl</c> - every other account (and a reverse-proxy
/// account with none configured) gets a null-<see cref="RedirectUrl"/> response, same as
/// this endpoint always returning bare 204 before this field existed. See
/// docs/auth.md's "Reverse proxy (trusted header)" section, "Known limitations".</summary>
public sealed record LogoutResponse
{
    public string? RedirectUrl { get; init; }
}

/// <summary>Response body for <c>GET /api/auth/bootstrap/status</c> - the dashboard's
/// route guard uses <see cref="AuthEnabled"/> to decide whether any login is required at
/// all (opt-in auth - see docs/auth.md), and (when it is) <see cref="NeedsBootstrap"/>/
/// <see cref="LocalEnabled"/>/<see cref="EntraEnabled"/>/<see cref="LdapEnabled"/>/
/// <see cref="OidcEnabled"/> to decide what <c>/login</c> should show.</summary>
public sealed record BootstrapStatusResponse
{
    /// <summary>The global switch - false means every endpoint in the app is open to
    /// anyone, no login of any kind required. See <c>ConditionalAuthorizationMiddlewareResultHandler</c>.</summary>
    public required bool AuthEnabled { get; init; }

    public required bool NeedsBootstrap { get; init; }

    public required bool LocalEnabled { get; init; }

    public required bool EntraEnabled { get; init; }

    public required bool LdapEnabled { get; init; }

    public required bool OidcEnabled { get; init; }

    /// <summary>The dashboard-configured button label for generic OIDC (e.g. "Okta") -
    /// unlike Entra's fixed "Sign in with Microsoft" wording, a generic provider has no
    /// built-in brand to hardcode. Null when <see cref="OidcEnabled"/> is false or no
    /// display name was ever set.</summary>
    public string? OidcDisplayName { get; init; }

    /// <summary>Whether reverse-proxy (trusted header) auth is configured+enabled - the
    /// dashboard's <c>/login</c> page calls <c>POST /api/auth/proxy/login</c>
    /// automatically when this is true, with no button/user action (see
    /// <c>ProxyAuthLoginEndpoints</c>'s own disabled-gate 404).</summary>
    public required bool ProxyAuthEnabled { get; init; }
}
