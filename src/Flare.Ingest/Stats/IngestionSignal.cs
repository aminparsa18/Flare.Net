namespace Flare.Ingest.Stats;

/// <summary>
/// The three OTLP signal types this receiver terminates - matches the three sinks
/// (<see cref="Sinks.ILogEventSink"/>/<see cref="Sinks.ISpanEventSink"/>/<see cref="Sinks.IMetricEventSink"/>)
/// and the six endpoint/service classes under <c>Otlp/</c> (one HTTP + one gRPC per signal).
/// </summary>
public enum IngestionSignal
{
    Logs,
    Traces,
    Metrics,
}
