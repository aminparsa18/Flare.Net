using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Attach a screenshot of the current page to exceptions (unhandled and <see cref="FlareMaui.RecordException"/>).
    /// Off by default: a screenshot can show anything the user typed or saw, so enable it only if that is acceptable.
    /// Uploaded to <c>/v1/screenshots</c>; at most 5 per launch, one per 10 seconds.
    /// </summary>
    public bool CaptureScreenshotOnError { get; set; }

    /// <summary>Largest screenshot uploaded, in bytes (the server accepts up to 512 KB). The capture lowers JPEG quality to fit.</summary>
    public int ScreenshotMaxBytes { get; set; } = 300 * 1024;

    /// <summary>Report an <c>app.hang</c> span when the UI thread is blocked for <see cref="AppHangThreshold"/>.</summary>
    public bool DetectAppHangs { get; set; } = true;

    /// <summary>How long the UI thread must be blocked to count as a hang. At least 500 ms.</summary>
    public TimeSpan AppHangThreshold { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Trace <c>HttpClient</c> calls and propagate <c>traceparent</c>.</summary>
    public bool InstrumentHttpClient { get; set; } = true;

    /// <summary>Record a <c>navigation</c> span for each Shell navigation.</summary>
    public bool TraceNavigation { get; set; } = true;

    /// <summary>Record lifecycle, tap, page and log breadcrumbs, shown on the session timeline.</summary>
    public bool Breadcrumbs { get; set; } = true;

    /// <summary>Put a tapped element's text and a page's title in breadcrumbs. Off by default: they can contain PII.</summary>
    public bool IncludeTextInBreadcrumbs { get; set; }

    /// <summary>Put the window or page title in breadcrumbs. Off by default: titles can contain PII.</summary>
    public bool IncludeTitleInBreadcrumbs { get; set; }

    /// <summary>Log records at or above this level also become breadcrumbs. <c>None</c> turns log breadcrumbs off.</summary>
    public LogLevel BreadcrumbLogLevel { get; set; } = LogLevel.Information;

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
        if (DetectAppHangs && AppHangThreshold < TimeSpan.FromMilliseconds(500))
            throw new InvalidOperationException("UseFlare: AppHangThreshold must be at least 500 ms.");
        if (CaptureScreenshotOnError && ScreenshotMaxBytes is < 10_000 or > 512 * 1024)
            throw new InvalidOperationException("UseFlare: ScreenshotMaxBytes must be between 10 KB and 512 KB.");
        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new InvalidOperationException("UseFlare: ServiceName is required.");
    }

    internal Uri SignalUri(string signal) =>
        new(Endpoint!.AbsoluteUri.TrimEnd('/') + "/v1/" + signal);
}
