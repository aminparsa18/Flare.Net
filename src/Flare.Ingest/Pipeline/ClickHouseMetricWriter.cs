using ClickHouse.Driver;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// <see cref="IClickHouseMetricWriter"/> backed by <c>Aspire.ClickHouse.Driver</c>'s
/// <see cref="IClickHouseClient"/>, same RowBinary bulk-insert approach as
/// <see cref="ClickHouseSpanWriter"/> - see its remarks for why <c>InsertBinaryAsync</c>
/// over hand-built SQL. One batch commonly spans all three point types (a single
/// <see cref="MetricFlushWorker"/> flush reads from the one shared <c>flare:metrics</c>
/// stream), so this issues up to four separate inserts per call - one per non-empty
/// point-type list, sequentially, same simple-first approach
/// <see cref="ClickHouseSpanWriter"/> took for its own single insert.
/// </summary>
public sealed class ClickHouseMetricWriter(IClickHouseClient client) : IClickHouseMetricWriter
{
    private const string GaugeTableName = "metrics_gauge";
    private const string SumTableName = "metrics_sum";
    private const string HistogramTableName = "metrics_histogram";
    private const string ExponentialHistogramTableName = "metrics_exponential_histogram";
    private const string ExemplarTableName = "metric_exemplars";

    public async Task WriteBatchAsync(
        IReadOnlyList<GaugePointRecord> gauges,
        IReadOnlyList<SumPointRecord> sums,
        IReadOnlyList<HistogramPointRecord> histograms,
        IReadOnlyList<ExponentialHistogramPointRecord> exponentialHistograms,
        CancellationToken cancellationToken = default)
    {
        if (gauges.Count > 0)
        {
            await client.InsertBinaryAsync(GaugeTableName, ClickHouseMetricRowMapper.GaugeColumns, ClickHouseMetricRowMapper.ToRows(gauges), cancellationToken: cancellationToken);
        }

        if (sums.Count > 0)
        {
            await client.InsertBinaryAsync(SumTableName, ClickHouseMetricRowMapper.SumColumns, ClickHouseMetricRowMapper.ToRows(sums), cancellationToken: cancellationToken);
        }

        if (histograms.Count > 0)
        {
            await client.InsertBinaryAsync(HistogramTableName, ClickHouseMetricRowMapper.HistogramColumns, ClickHouseMetricRowMapper.ToRows(histograms), cancellationToken: cancellationToken);
        }

        if (exponentialHistograms.Count > 0)
        {
            await client.InsertBinaryAsync(ExponentialHistogramTableName, ClickHouseMetricRowMapper.ExponentialHistogramColumns, ClickHouseMetricRowMapper.ToRows(exponentialHistograms), cancellationToken: cancellationToken);
        }

        // Exemplars go last: a retry after a failure here re-inserts the points above (at-least-once, like
        // every other write in this batch) and re-attempts the exemplars (ADR-0241).
        var exemplarRows = ClickHouseMetricRowMapper.ToExemplarRows(
            gauges.Cast<MetricPointRecord>().Concat(sums).Concat(histograms).Concat(exponentialHistograms));
        if (exemplarRows.Count > 0)
        {
            await client.InsertBinaryAsync(ExemplarTableName, ClickHouseMetricRowMapper.ExemplarColumns, exemplarRows, cancellationToken: cancellationToken);
        }
    }
}
