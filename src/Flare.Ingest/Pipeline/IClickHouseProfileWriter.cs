using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Writes a batch of <see cref="ProfileSampleRecord"/>s to ClickHouse. Behind an interface so
/// <see cref="ProfileFlushWorker"/> is unit-testable against a fake, same as
/// <see cref="IClickHouseSpanWriter"/>.
/// </summary>
public interface IClickHouseProfileWriter
{
    /// <summary>Inserts <paramref name="samples"/> as one batch; throws on failure (the worker leaves entries un-acked for retry).</summary>
    Task WriteBatchAsync(IReadOnlyList<ProfileSampleRecord> samples, CancellationToken cancellationToken = default);
}
