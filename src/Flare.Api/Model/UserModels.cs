using Flare.Identity.Users;
using MemoryPack;

namespace Flare.Api.Model;

/// <summary>A row in the Admin-only "manage users" screen. Same fields as
/// <see cref="AuthUserDto"/> plus what an Admin actually needs to act on -
/// <c>IsDisabled</c> and <c>CreatedAt</c> - deliberately still never a password hash.</summary>
[MemoryPackable]
public sealed partial record UserSummaryDto
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required UserRole Role { get; init; }

    public required string AuthProvider { get; init; }

    public required bool IsDisabled { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Response body for <c>GET /api/users</c>.</summary>
[MemoryPackable]
public sealed partial record UserListResponse
{
    public required IReadOnlyList<UserSummaryDto> Users { get; init; }
}

/// <summary>Request body for <c>PATCH /api/users/{id}/role</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record SetUserRoleRequest
{
    public required UserRole Role { get; init; }
}

/// <summary>Request body for <c>PATCH /api/users/{id}/disabled</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record SetUserDisabledRequest
{
    public required bool IsDisabled { get; init; }
}
