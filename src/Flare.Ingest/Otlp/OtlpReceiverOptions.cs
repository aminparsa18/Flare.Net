namespace Flare.Ingest.Otlp;

/// <summary>
/// Limits on the OTLP receivers, bound from the <c>Otlp</c> configuration section (e.g.
/// <c>Otlp__MaxRequestSizeBytes=134217728</c>). Read once at startup - it sizes Kestrel and
/// the gRPC server, neither of which re-reads its limits at runtime.
/// </summary>
public sealed class OtlpReceiverOptions
{
    public const string SectionName = "Otlp";

    /// <summary>64 MiB - the OpenTelemetry spec's recommended OTLP request cap, and the
    /// .NET SDK exporter's own default since 1.18.</summary>
    public const long DefaultMaxRequestSizeBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Largest single export Flare accepts, applied as both the gRPC server's
    /// <c>MaxReceiveMessageSize</c> (default 4 MB otherwise) and Kestrel's
    /// <c>MaxRequestBodySize</c> (default ~30 MB otherwise). Matching the exporter's cap
    /// matters because an exporter treats a size rejection as non-retryable and drops the
    /// whole batch. The HTTP receivers buffer each body in memory, so this is also the
    /// per-request memory ceiling. Must fit in an <see cref="int"/> (the gRPC limit's type).
    /// </summary>
    public long MaxRequestSizeBytes { get; set; } = DefaultMaxRequestSizeBytes;
}
