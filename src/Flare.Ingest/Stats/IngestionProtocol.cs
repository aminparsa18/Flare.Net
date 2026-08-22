namespace Flare.Ingest.Stats;

/// <summary>
/// How a signal reached this receiver: the two push-based OTLP transports on shared
/// Kestrel listeners (:4317 gRPC, :4318 HTTP - see <c>Program.cs</c>'s
/// <c>ConfigureKestrel</c> call), or <see cref="Scrape"/> - <see cref="Prometheus.PrometheusScrapeWorker"/>
/// pulling from a configured target instead of something pushing to Flare. Metrics-only in
/// practice (Logs/Traces never scrape), but this is a signal/protocol cross-product same as
/// <see cref="Grpc"/>/<see cref="Http"/> - <see cref="IngestionStatsKeys.FieldPrefix"/> and
/// the read side's dense per-minute bucket grid don't special-case which signals actually
/// use which protocol.
/// </summary>
public enum IngestionProtocol
{
    Grpc,
    Http,
    Scrape,
}
