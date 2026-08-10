namespace Flare.Ingest.Stats;

/// <summary>
/// Records per-signal/per-protocol ingestion counters and recent-rejection events for the
/// dashboard's Ingestion page. Deliberately separate from <see cref="Sinks.ILogEventSink"/>
/// and friends: those durably buffer the actual event data toward ClickHouse; this is
/// best-effort operational telemetry about the receiving process itself (accepted/rejected
/// counts, bytes), not something that needs at-least-once delivery guarantees.
/// </summary>
public interface IIngestionStatsTracker
{
    /// <summary>
    /// Call once per successfully parsed+mapped export request. <paramref name="recordCount"/>
    /// is the number of individual records within the export (log records / spans / metric
    /// data points, not export requests) - what "events/minute" should mean on the
    /// dashboard, matching Seq's own ingestion-page semantics.
    /// </summary>
    ValueTask RecordAcceptedAsync(
        IngestionSignal signal,
        IngestionProtocol protocol,
        int recordCount,
        long byteCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Call once per export request that couldn't be parsed/mapped at all (the whole
    /// request is rejected, not a partial record count - OTLP gives no clean way to
    /// reject individual records within one request at this layer).
    /// </summary>
    ValueTask RecordRejectedAsync(
        IngestionSignal signal,
        IngestionProtocol protocol,
        string reason,
        CancellationToken cancellationToken = default);
}
