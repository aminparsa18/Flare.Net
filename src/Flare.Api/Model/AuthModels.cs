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

    /// <summary>"Local", "Entra", or "ActiveDirectory" - lets the dashboard show e.g. a
    /// disabled password field for an SSO account without a second round-trip.</summary>
    public required string AuthProvider { get; init; }
}

/// <summary>Response body for <c>GET /api/auth/bootstrap/status</c> - the dashboard's
/// route guard uses <see cref="AuthEnabled"/> to decide whether any login is required at
/// all (opt-in auth - see docs/auth.md), and (when it is) <see cref="NeedsBootstrap"/>/
/// <see cref="LocalEnabled"/>/<see cref="EntraEnabled"/>/<see cref="LdapEnabled"/> to
/// decide what <c>/login</c> should show.</summary>
public sealed record BootstrapStatusResponse
{
    /// <summary>The global switch - false means every endpoint in the app is open to
    /// anyone, no login of any kind required. See <c>ConditionalAuthorizationMiddlewareResultHandler</c>.</summary>
    public required bool AuthEnabled { get; init; }

    public required bool NeedsBootstrap { get; init; }

    public required bool LocalEnabled { get; init; }

    public required bool EntraEnabled { get; init; }

    public required bool LdapEnabled { get; init; }
}
