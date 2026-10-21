using OpenTelemetry.Exporter;

namespace Flare.OpenTelemetry;

/// <summary>
/// Settings for <see cref="Microsoft.Extensions.Hosting.FlareOpenTelemetryExtensions.AddFlareTelemetry(Microsoft.Extensions.Hosting.IHostApplicationBuilder, Action{FlareOptions}?)"/>.
/// Bound from the <c>Flare</c> configuration section first (so an environment variable such as
/// <c>Flare__Endpoint</c> works), then overridden by whatever the configure delegate sets.
/// </summary>
public sealed class FlareOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Flare";

    /// <summary>Flare.Ingest's conventional OTLP/gRPC address, used when no endpoint is configured.</summary>
    public static readonly Uri DefaultEndpoint = new("http://localhost:4317");

    /// <summary>
    /// Flare.Ingest's OTLP endpoint: the gRPC port (<c>4317</c>) or, with <see cref="Protocol"/>
    /// set to <see cref="OtlpExportProtocol.HttpProtobuf"/>, the HTTP port (<c>4318</c>) without a
    /// signal path (<c>/v1/traces</c> and the others are appended). Defaults to <see cref="DefaultEndpoint"/>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>OTLP transport. Defaults to gRPC; use HTTP/protobuf where gRPC is blocked by a proxy.</summary>
    public OtlpExportProtocol Protocol { get; set; } = OtlpExportProtocol.Grpc;

    /// <summary>
    /// Ingest key, sent as <c>Authorization: Bearer</c> on every export. Only needed when
    /// Flare.Ingest runs with <c>Auth:IngestKeyRequired</c>. Unset sends no header.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The <c>service.name</c> resource attribute. Unset leaves whatever the app's own OpenTelemetry resource already says.</summary>
    public string? ServiceName { get; set; }

    /// <summary>The <c>service.version</c> resource attribute. Unset leaves the SDK default.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Fraction of new traces to record, from 0 to 1. Defaults to 1 (record everything). Below 1 a
    /// parent-based ratio sampler is installed: the root service decides, and every service that
    /// joins its trace follows that decision, so a sampled trace stays whole.
    /// </summary>
    public double TraceSampleRatio { get; set; } = 1.0;

    /// <summary>Register ASP.NET Core instrumentation for traces and metrics. On by default.</summary>
    public bool InstrumentAspNetCore { get; set; } = true;

    /// <summary>Register <c>HttpClient</c> instrumentation for traces and metrics. On by default.</summary>
    public bool InstrumentHttpClient { get; set; } = true;

    /// <summary>Register .NET runtime metrics (GC, thread pool, exceptions). On by default.</summary>
    public bool InstrumentRuntime { get; set; } = true;

    /// <summary>Extra <see cref="System.Diagnostics.ActivitySource"/> names to trace, for the app's own spans.</summary>
    public IList<string> ActivitySources { get; set; } = [];

    /// <summary>Extra <see cref="System.Diagnostics.Metrics.Meter"/> names to export, for the app's own metrics.</summary>
    public IList<string> Meters { get; set; } = [];
}
