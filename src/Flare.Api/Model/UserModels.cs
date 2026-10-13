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

/// <summary>Request body for <c>POST /api/service-accounts</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record CreateServiceAccountRequest
{
    public required string Name { get; init; }

    public required UserRole Role { get; init; }
}

/// <summary>Request body for <c>POST /api/users/invite</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record InviteUserRequest
{
    public required string Username { get; init; }

    public required UserRole Role { get; init; }
}

/// <summary>Response for invite / admin password reset: the one-time token to put in a
/// <c>/set-password?token=</c> link. Shown once; only its hash is stored.</summary>
[MemoryPackable]
public sealed partial record PasswordSetLinkResponse
{
    public required UserSummaryDto User { get; init; }

    public required string Token { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>True when the link was also emailed to the account (invite, SMTP configured, email username).</summary>
    public bool EmailSent { get; init; }
}

/// <summary>Request body for <c>POST /api/users/invite/bulk</c> (ADR-0115).</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record BulkInviteRequest
{
    public required string[] Usernames { get; init; }

    public required UserRole Role { get; init; }
}

/// <summary>One line of a bulk invite result. <see cref="Status"/> is <c>Created</c>, <c>Exists</c> or
/// <c>Invalid</c>; <see cref="Token"/> is set only for <c>Created</c>.</summary>
[MemoryPackable]
public sealed partial record BulkInviteResultItem
{
    public required string Username { get; init; }

    public required string Status { get; init; }

    public string? Token { get; init; }

    public bool EmailSent { get; init; }
}

/// <summary>Response for <c>POST /api/users/invite/bulk</c>.</summary>
[MemoryPackable]
public sealed partial record BulkInviteResponse
{
    public required DateTimeOffset ExpiresAt { get; init; }

    public required BulkInviteResultItem[] Results { get; init; }
}
