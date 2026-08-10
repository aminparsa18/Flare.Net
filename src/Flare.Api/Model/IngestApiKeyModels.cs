namespace Flare.Api.Model;

/// <summary>An ingest API key as returned by list/create - never carries the raw key
/// value except at creation time (see <see cref="CreateIngestApiKeyResponse"/>).</summary>
public sealed record IngestApiKeyDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public required bool IsActive { get; init; }
}

/// <summary>Request body for <c>POST /api/ingest-keys</c>.</summary>
public sealed record CreateIngestApiKeyRequest
{
    public required string Name { get; init; }
}

/// <summary><see cref="RawKey"/> is shown exactly once, here - Flare never stores or
/// displays it again after this response (see <see cref="Identity.IngestKeys.SqliteIngestApiKeyStore"/>).</summary>
public sealed record CreateIngestApiKeyResponse
{
    public required IngestApiKeyDto Key { get; init; }

    public required string RawKey { get; init; }
}

/// <summary>Response body for <c>GET /api/ingest-keys</c>.</summary>
public sealed record IngestApiKeyListResponse
{
    public required IReadOnlyList<IngestApiKeyDto> Keys { get; init; }
}
