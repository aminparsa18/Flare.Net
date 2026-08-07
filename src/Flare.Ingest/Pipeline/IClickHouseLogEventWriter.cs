using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Writes a batch of <see cref="LogEvent"/>s to ClickHouse. Abstracted behind an
/// interface so <see cref="ClickHouseFlushWorker"/>'s batching/flush logic is
/// unit-testable against a fake, without a live ClickHouse connection.
/// </summary>
public interface IClickHouseLogEventWriter
{
    /// <summary>
    /// Inserts <paramref name="events"/> as a single batch. Throws on failure - callers
    /// are responsible for retry semantics (see <see cref="ClickHouseFlushWorker"/>,
    /// which deliberately doesn't XACK on failure so entries are retried via the
    /// consumer group's pending-entries list).
    /// </summary>
    Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken = default);
}
