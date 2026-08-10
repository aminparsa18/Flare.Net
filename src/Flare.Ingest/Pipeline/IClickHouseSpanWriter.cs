using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Writes a batch of <see cref="SpanRecord"/>s to ClickHouse. Abstracted behind an
/// interface so <see cref="SpanFlushWorker"/>'s batching/flush logic is unit-testable
/// against a fake, same shape as <see cref="IClickHouseLogEventWriter"/>.
/// </summary>
public interface IClickHouseSpanWriter
{
    /// <summary>
    /// Inserts <paramref name="spans"/> as a single batch. Throws on failure - callers
    /// are responsible for retry semantics (see <see cref="SpanFlushWorker"/>, which
    /// deliberately doesn't XACK on failure so entries are retried via the consumer
    /// group's pending-entries list).
    /// </summary>
    Task WriteBatchAsync(IReadOnlyList<SpanRecord> spans, CancellationToken cancellationToken = default);
}
