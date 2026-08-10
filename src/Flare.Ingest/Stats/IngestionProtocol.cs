namespace Flare.Ingest.Stats;

/// <summary>
/// The two OTLP transports this receiver terminates on shared Kestrel listeners
/// (:4317 gRPC, :4318 HTTP) - see <c>Program.cs</c>'s <c>ConfigureKestrel</c> call.
/// </summary>
public enum IngestionProtocol
{
    Grpc,
    Http,
}
