namespace Flare.Maui;

/// <summary>Settings for <c>MauiAppBuilder.UseFlare</c>.</summary>
public sealed class FlareMauiOptions
{
    /// <summary>Base URL of Flare.Ingest's OTLP/HTTP receiver (port 4318), without a signal path.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>The <c>service.name</c> resource attribute. Pin the ingest key to this name.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Overrides <c>service.version</c>. Defaults to the app's version string.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Optional ingest key, sent as a bearer token. It ships in the app binary, so treat it as public.</summary>
    public string? IngestKey { get; set; }

    /// <summary>Retry failed exports from disk after a restart, using the OpenTelemetry exporter's disk retry (experimental in the SDK).</summary>
    public bool EnableOfflineQueue { get; set; } = true;

    /// <summary>Record AppDomain and unobserved-task exceptions as <c>app.unhandled_exception</c> spans.</summary>
    public bool CaptureUnhandledExceptions { get; set; } = true;

    /// <summary>Trace <c>HttpClient</c> calls and propagate <c>traceparent</c>.</summary>
    public bool InstrumentHttpClient { get; set; } = true;

    /// <summary>Record a <c>navigation</c> span for each Shell navigation.</summary>
    public bool TraceNavigation { get; set; } = true;

    /// <summary>Export <c>ILogger</c> output.</summary>
    public bool ExportLogs { get; set; } = true;

    /// <summary>Export metrics (HttpClient metrics and any meter named in <see cref="AdditionalMeters"/>).</summary>
    public bool ExportMetrics { get; set; } = true;

    /// <summary>Extra <c>ActivitySource</c> names to export, for the app's own spans.</summary>
    public IList<string> AdditionalSources { get; } = new List<string>();

    /// <summary>Extra <c>Meter</c> names to export.</summary>
    public IList<string> AdditionalMeters { get; } = new List<string>();

    internal void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("UseFlare: Endpoint must be an absolute http(s) URL of the OTLP/HTTP receiver.");
        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new InvalidOperationException("UseFlare: ServiceName is required.");
    }

    internal Uri SignalUri(string signal) =>
        new(Endpoint!.AbsoluteUri.TrimEnd('/') + "/v1/" + signal);
}
