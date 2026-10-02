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
}

public sealed class AuditEventListResponse
{
    public IReadOnlyList<AuditEventDto> Events { get; init; } = [];
    /// <summary>Pass back as <c>before</c> for the next (older) page; null on the last page.</summary>
    public long? NextBefore { get; init; }
}
