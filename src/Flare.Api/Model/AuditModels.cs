namespace Flare.Api.Model;

public sealed class AuditEventDto
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public Guid? ActorId { get; init; }
    public string ActorName { get; init; } = string.Empty;
    /// <summary><c>session</c> or <c>pat</c>.</summary>
    public string ActorKind { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string? ResourceId { get; init; }
    public string Route { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string? SourceIp { get; init; }
    /// <summary>Changed fields for an update (secrets already redacted); empty when none were reported.</summary>
    public IReadOnlyList<AuditFieldChangeDto> Changes { get; init; } = [];
    /// <summary>Hash chain link (ADR-0202); null for events written before the chain existed.</summary>
    public string? PrevHash { get; init; }
    public string? Hash { get; init; }
}

public sealed class AuditFieldChangeDto
{
    public string Field { get; init; } = string.Empty;
    public string? Before { get; init; }
    public string? After { get; init; }
}

public sealed class AuditEventListResponse
{
    public IReadOnlyList<AuditEventDto> Events { get; init; } = [];
    /// <summary>Pass back as <c>before</c> for the next (older) page; null on the last page.</summary>
    public long? NextBefore { get; init; }
}

/// <summary>
/// One row of <c>GET /api/audit-events/export</c> (JSON and NDJSON). Unlike <see cref="AuditEventDto"/>
/// it carries the stored text of every hashed column (<c>timestamp</c> in round-trip UTC form,
/// <c>changes</c> as the raw JSON string) so a verifier can recompute <see cref="Hash"/> offline.
/// </summary>
public sealed class AuditExportRowDto
{
    public long Id { get; init; }
    public string Timestamp { get; init; } = string.Empty;
    public string? ActorId { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public string ActorKind { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string? ResourceId { get; init; }
    public string Route { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string? SourceIp { get; init; }
    public string? Changes { get; init; }
    public string? PrevHash { get; init; }
    public string? Hash { get; init; }
}

public sealed class AuditChainVerificationDto
{
    public bool Valid { get; init; }
    public long ChainedEvents { get; init; }
    /// <summary>Events written before the hash chain existed; outside it and unverifiable.</summary>
    public long LegacyEvents { get; init; }
    public long? FirstBrokenId { get; init; }
    public string? Reason { get; init; }
    /// <summary>Hash of the newest chained event. Record it elsewhere to detect later truncation.</summary>
    public string? HeadHash { get; init; }
}
