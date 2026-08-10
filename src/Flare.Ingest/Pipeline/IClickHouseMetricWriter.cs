using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Writes a batch of metric data points to ClickHouse, split by point type across the
/// three tables (<c>metrics_gauge</c>/<c>metrics_sum</c>/<c>metrics_histogram</c>).
/// Abstracted behind an interface so <see cref="MetricFlushWorker"/>'s batching/flush
/// logic is unit-testable against a fake, same shape as <see cref="IClickHouseSpanWriter"/>.
/// </summary>
public interface IClickHouseMetricWriter
{
    /// <summary>
    /// Inserts each non-empty list as its own batch (one <c>InsertBinaryAsync</c> call
    /// per non-empty table). Throws on failure - callers are responsible for retry
    /// semantics (see <see cref="MetricFlushWorker"/>, which deliberately doesn't XACK
    /// on failure so entries are retried via the consumer group's pending-entries list).
    /// </summary>
    Task WriteBatchAsync(
        IReadOnlyList<GaugePointRecord> gauges,
        IReadOnlyList<SumPointRecord> sums,
        IReadOnlyList<HistogramPointRecord> histograms,
        CancellationToken cancellationToken = default);
}
