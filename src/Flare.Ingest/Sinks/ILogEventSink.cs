using Flare.Ingest.Model;

namespace Flare.Ingest.Sinks;

/// <summary>
/// The seam between the OTLP receiver and whatever eventually persists log events.
/// </summary>
/// <remarks>
/// Implemented by <see cref="RedisStreamLogEventSink"/>, which buffers into a Redis
/// Stream that <see cref="Pipeline.ClickHouseFlushWorker"/> reads from and batch-inserts
/// into ClickHouse. Callers (<c>OtlpGrpcLogsService</c>, <c>OtlpHttpLogsEndpoints</c>)
/// only ever depend on this interface, not the concrete sink.
/// </remarks>
public interface ILogEventSink
{
    ValueTask WriteAsync(LogEvent logEvent, CancellationToken cancellationToken = default);
}