using Flare.Ingest.Model;

namespace Flare.Ingest.Sinks;

/// <summary>
/// The seam between the OTLP receiver and whatever eventually persists log events.
/// </summary>
/// <remarks>
/// This roadmap item ("OTLP logs receiver") only ships <see cref="ConsoleLogEventSink"/>
/// as a throwaway placeholder. The batched ClickHouse insert pipeline (a later roadmap
/// item) replaces only the DI registration of this interface - no caller changes needed.
/// </remarks>
public interface ILogEventSink
{
    ValueTask WriteAsync(LogEvent logEvent, CancellationToken cancellationToken = default);
}