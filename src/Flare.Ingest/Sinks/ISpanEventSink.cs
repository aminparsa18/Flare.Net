using Flare.Ingest.Model;

namespace Flare.Ingest.Sinks;

/// <summary>
/// The seam between the OTLP trace receiver and whatever eventually persists spans.
/// </summary>
/// <remarks>
/// Implemented by <see cref="RedisStreamSpanEventSink"/>, which buffers into a Redis
/// Stream that <see cref="Pipeline.SpanFlushWorker"/> reads from and batch-inserts into
/// ClickHouse - the same shape as <see cref="ILogEventSink"/>/<see cref="RedisStreamLogEventSink"/>,
/// deliberately not unified with it (see <see cref="Pipeline.SpanFlushWorker"/>'s remarks
/// for why the two pipelines stay parallel rather than sharing a generic base).
/// </remarks>
public interface ISpanEventSink
{
    ValueTask WriteAsync(SpanRecord span, CancellationToken cancellationToken = default);
}
