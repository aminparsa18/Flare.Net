using MemoryPack;

namespace Flare.Api.Model;

/// <summary>A personal access token as returned by list/create - never carries the raw
/// token value except at creation time (see <see cref="CreateAccessTokenResponse"/>).</summary>
[MemoryPackable]
public sealed partial record AccessTokenDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public required bool IsActive { get; init; }
}

/// <summary>Request body for <c>POST /api/access-tokens</c>. <see cref="ExpiresInDays"/>
/// null means the token never expires (matches ingest API keys' own no-expiry precedent) -
/// see <see cref="Endpoints.PersonalAccessTokenEndpoints"/>'s remarks for the validation
/// range.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record CreateAccessTokenRequest
{
    public required string Name { get; init; }

    public int? ExpiresInDays { get; init; }
}

/// <summary><see cref="RawToken"/> is shown exactly once, here - Flare never stores or
/// displays it again after this response (see
/// <see cref="Identity.PersonalAccessTokens.SqlitePersonalAccessTokenStore"/>).</summary>
[MemoryPackable]
public sealed partial record CreateAccessTokenResponse
{
    public required AccessTokenDto Token { get; init; }

    public required string RawToken { get; init; }
}

/// <summary>Response body for <c>GET /api/access-tokens</c> - the caller's own tokens
/// only, see that endpoint's remarks.</summary>
[MemoryPackable]
public sealed partial record AccessTokenListResponse
{
    public required IReadOnlyList<AccessTokenDto> Tokens { get; init; }
}
