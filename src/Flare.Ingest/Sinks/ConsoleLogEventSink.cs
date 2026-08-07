using Flare.Ingest.Model;

namespace Flare.Ingest.Sinks;

/// <summary>
/// Throwaway placeholder sink: logs one line per received event so ingestion is
/// visible during local dev. Not durable, not batched, not the real destination -
/// swapped out entirely once the ClickHouse batching pipeline lands.
/// </summary>
public sealed class ConsoleLogEventSink(ILogger<ConsoleLogEventSink> logger) : ILogEventSink
{
    public ValueTask WriteAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[{Severity}] {Service} {Body} trace={TraceId}",
            logEvent.SeverityText ?? logEvent.SeverityNumber.ToString(),
            logEvent.ServiceName ?? "(unknown)",
            logEvent.Body,
            logEvent.TraceId ?? "(none)");

        return ValueTask.CompletedTask;
    }
}