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
}

/// <summary>Response body for <c>GET /api/auth/bootstrap/status</c> - the dashboard uses
/// this to decide whether to show <c>/setup</c> (create the first Admin) or <c>/login</c>.</summary>
public sealed record BootstrapStatusResponse
{
    public required bool NeedsBootstrap { get; init; }
}
