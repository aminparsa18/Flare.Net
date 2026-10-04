using MemoryPack;

namespace Flare.Api.Model;

/// <summary>One of the caller's own login sessions. <see cref="Id"/> is a one-way handle
/// derived from the session token, never the token itself (which is the cookie value and
/// would be a credential if listed).</summary>
[MemoryPackable]
public sealed partial record SessionDto
{
    public required string Id { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset LastSeenAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>True for the session making this request.</summary>
    public required bool IsCurrent { get; init; }
}

/// <summary>Response body for <c>GET /api/auth/sessions</c>.</summary>
[MemoryPackable]
public sealed partial record SessionListResponse
{
    public required IReadOnlyList<SessionDto> Sessions { get; init; }
}
