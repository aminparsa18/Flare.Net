using Flare.Ingest.Model;

namespace Flare.Ingest.Sinks;

/// <summary>
/// The seam between the OTLP metrics receiver and whatever eventually persists metric
/// data points.
/// </summary>
/// <remarks>
/// Implemented by <see cref="RedisStreamMetricEventSink"/>, which buffers into a single
/// Redis Stream that <see cref="Pipeline.MetricFlushWorker"/> reads from and fans out
/// into three ClickHouse tables (gauge/sum/histogram) at flush time - see
/// <see cref="Pipeline.MetricFlushWorker"/>'s remarks for why one shared stream, not
/// three parallel ones like logs vs. spans.
/// </remarks>
public interface IMetricEventSink
{
    ValueTask WriteAsync(MetricPointRecord point, CancellationToken cancellationToken = default);
}
