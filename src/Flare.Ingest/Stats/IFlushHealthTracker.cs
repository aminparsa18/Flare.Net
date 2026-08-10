namespace Flare.Ingest.Stats;

/// <summary>
/// Records each flush worker's own last-flush outcome for the Ingestion page's pipeline-
/// health section (Planning.md v10). Deliberately separate from <see cref="IIngestionStatsTracker"/>:
/// that one is written from the hot per-request OTLP receive path (six call sites); this is
/// written once per flush cycle (three call sites - one per <c>*FlushWorker</c>), a
/// materially lower rate that doesn't need the same <c>IBatch</c>-per-call discipline,
/// though it still uses one for the same "single Redis round trip" reason.
/// </summary>
public interface IFlushHealthTracker
{
    /// <summary>Call after a successful flush. Resets <c>consecutiveErrors</c> to 0.</summary>
    ValueTask RecordSuccessAsync(
        IngestionSignal signal,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Call after a failed flush (the batch stays un-acked for PEL reclaim - see
    /// <c>ClickHouseFlushWorker</c>'s remarks). Increments <c>consecutiveErrors</c>; does
    /// not touch <c>lastFlushAt</c>/<c>lastBatchSize</c>, so those keep reflecting the last
    /// time data actually reached ClickHouse, not the last attempt.
    /// </summary>
    ValueTask RecordFailureAsync(
        IngestionSignal signal,
        string error,
        CancellationToken cancellationToken = default);
}
