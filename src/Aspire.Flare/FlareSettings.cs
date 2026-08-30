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

    /// <summary>
    /// Gets or sets the ingest API key to attach as an <c>Authorization: Bearer</c>
    /// header on every export, if Flare.Ingest's receiving instance has
    /// <c>Auth:IngestKeyRequired</c> set. Unset by default - anonymous ingest, matching
    /// today's default on the Flare.Ingest side. Bound from the <c>Aspire:Flare</c>
    /// configuration section, same as <see cref="Endpoint"/>/<see cref="Protocol"/>; no
    /// automatic flow-through from <c>.WithReference(flare)</c>'s connection string yet
    /// (unlike <see cref="Endpoint"/>) - set it explicitly via <c>configureSettings</c> or
    /// configuration, e.g. mirroring whatever value was passed to
    /// <c>Aspire.Hosting.Flare</c>'s <c>AddFlare(...).WithApiKey(...)</c> on the AppHost side.
    /// </summary>
    public string? ApiKey { get; set; }
}
