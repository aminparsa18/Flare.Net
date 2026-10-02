namespace Flare.Identity.Audit;

/// <summary>
/// Append-only audit trail of state-changing requests - see
/// docs-internal/adr/0079-audit-log.md. Deliberately has no update method.
/// </summary>
public interface IAuditEventStore
{
    Task AppendAsync(NewAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>Newest first. <paramref name="beforeId"/> is the keyset cursor: pass the
    /// last row's <see cref="AuditEvent.Id"/> from the previous page to get the next one.</summary>
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        AuditEventFilter filter,
        long? beforeId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Retention: deletes events older than <paramref name="cutoff"/> and returns how many.</summary>
    Task<int> PruneAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
