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

    /// <summary>Walks the whole hash chain oldest to newest and reports the first row that
    /// does not verify (ADR-0202). The first chained row's <c>PrevHash</c> is trusted as the
    /// anchor, because retention deletes the rows before it.</summary>
    Task<AuditChainVerification> VerifyChainAsync(CancellationToken cancellationToken = default);

    /// <summary>Oldest first, strictly after <paramref name="afterId"/>: the forwarding sinks' read (ADR-0203).</summary>
    Task<IReadOnlyList<AuditEvent>> QueryAfterAsync(long afterId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Highest event id the named forwarding sink has delivered; 0 when it has not started.</summary>
    Task<long> GetForwardingCursorAsync(string sink, CancellationToken cancellationToken = default);

    Task SetForwardingCursorAsync(string sink, long lastId, CancellationToken cancellationToken = default);

    /// <summary>Retention: deletes events older than <paramref name="cutoff"/> and returns how many.</summary>
    Task<int> PruneAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
