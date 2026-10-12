namespace Flare.Identity.Audit;

/// <summary>One recorded state change (ADR-0079). <c>Changes</c> is a JSON array of {field, before, after}, already redacted (ADR-0081).</summary>
public sealed record AuditEvent(
    long Id,
    DateTimeOffset Timestamp,
    Guid? ActorId,
    string ActorName,
    string ActorKind,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Route,
    int StatusCode,
    string? SourceIp,
    string? Changes = null,
    string? PrevHash = null,
    string? Hash = null);

/// <summary>An <see cref="AuditEvent"/> before the store assigns its id.</summary>
public sealed record NewAuditEvent(
    DateTimeOffset Timestamp,
    Guid? ActorId,
    string ActorName,
    string ActorKind,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Route,
    int StatusCode,
    string? SourceIp,
    string? Changes = null);

/// <summary>Filters for <see cref="IAuditEventStore.QueryAsync"/>; every member is optional.</summary>
public sealed record AuditEventFilter
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? ActorId { get; init; }
    public string? ResourceType { get; init; }
    public string? Action { get; init; }
}

/// <summary>
/// Result of walking the audit hash chain (ADR-0202). <see cref="Valid"/> is false when a
/// chained row's hash does not match its content or its link to the previous row.
/// </summary>
public sealed record AuditChainVerification(
    bool Valid,
    long ChainedEvents,
    long LegacyEvents,
    long? FirstBrokenId,
    string? Reason,
    string? HeadHash);
