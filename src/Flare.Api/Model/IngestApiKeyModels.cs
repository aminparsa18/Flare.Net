using MemoryPack;

namespace Flare.Api.Model;

/// <summary>An ingest API key as returned by list/create - never carries the raw key
/// value except at creation time (see <see cref="CreateIngestApiKeyResponse"/>).
/// Carries the key's ingestion limits and its current usage (ADR-0051) - usage is read
/// live from the same Redis counters <c>Flare.Ingest</c> enforces against, so it's the
/// current UTC minute/day, not a history. New members are appended, never inserted -
/// the dashboard's hand-written <c>$lib/memorypack/IngestApiKeyDto.ts</c> reads them in
/// declared order.</summary>
[MemoryPackable]
public sealed partial record IngestApiKeyDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public required bool IsActive { get; init; }

    public bool LimitsEnabled { get; init; }

    public long? MaxEventsPerMinute { get; init; }

    public long? MaxBytesPerMinute { get; init; }

    public long? MaxEventsPerDay { get; init; }

    public long? MaxBytesPerDay { get; init; }

    public long EventsThisMinute { get; init; }

    public long BytesThisMinute { get; init; }

    public long EventsToday { get; init; }

    public long BytesToday { get; init; }
}

/// <summary>Request body for <c>PUT /api/ingest-keys/{id}/limits</c> - replaces all five
/// limit settings at once. A null cap means "no cap on this dimension"; a set one must be
/// positive.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record UpdateIngestApiKeyLimitsRequest
{
    public required bool LimitsEnabled { get; init; }

    public long? MaxEventsPerMinute { get; init; }

    public long? MaxBytesPerMinute { get; init; }

    public long? MaxEventsPerDay { get; init; }

    public long? MaxBytesPerDay { get; init; }
}

/// <summary>Request body for <c>POST /api/ingest-keys</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record CreateIngestApiKeyRequest
{
    public required string Name { get; init; }
}

/// <summary><see cref="RawKey"/> is shown exactly once, here - Flare never stores or
/// displays it again after this response (see <see cref="Identity.IngestKeys.SqliteIngestApiKeyStore"/>).</summary>
[MemoryPackable]
public sealed partial record CreateIngestApiKeyResponse
{
    public required IngestApiKeyDto Key { get; init; }

    public required string RawKey { get; init; }
}

/// <summary>Response body for <c>GET /api/ingest-keys</c>.</summary>
[MemoryPackable]
public sealed partial record IngestApiKeyListResponse
{
    public required IReadOnlyList<IngestApiKeyDto> Keys { get; init; }
}
