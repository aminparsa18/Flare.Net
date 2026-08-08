using OpenTelemetry.Exporter;

namespace Aspire.Flare;

/// <summary>
/// Provides the client configuration settings for connecting a project's OpenTelemetry log
/// events to Flare.Ingest's OTLP receiver. Bound from the <c>Aspire:Flare</c> configuration
/// section, then overridden by <see cref="Microsoft.Extensions.Hosting.AspireFlareExtensions.AddFlareOtlpExporter"/>'s
/// resolved connection string, then by any caller-supplied configure delegate - same precedence
/// order <c>Aspire.Seq.SeqSettings</c> uses.
/// </summary>
public sealed class FlareSettings
{
    /// <summary>
    /// Gets or sets Flare.Ingest's OTLP endpoint. Normally left unset and resolved from the
    /// <c>{connectionName}</c> connection string injected by <c>.WithReference(flare)</c> on the
    /// AppHost side (see <c>Flare.Hosting.Aspire</c>'s <c>FlareResource.ConnectionStringExpression</c>).
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the OTLP protocol to use against <see cref="Endpoint"/>. Defaults to
    /// <see cref="OtlpExportProtocol.Grpc"/>, matching <c>FlareResource</c>'s connection string
    /// (Flare.Ingest's OTLP/gRPC endpoint) and <c>WithOtlpEndpoint</c>'s <c>useHttp: false</c>
    /// default on the AppHost side.
    /// </summary>
    public OtlpExportProtocol Protocol { get; set; } = OtlpExportProtocol.Grpc;
}
