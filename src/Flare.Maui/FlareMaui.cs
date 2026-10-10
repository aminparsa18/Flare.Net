using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Flare.Maui;

/// <summary>
/// Owns the telemetry providers. MAUI has no generic host, so nothing starts the usual hosted
/// OpenTelemetry service: the providers are built here and kept for the life of the process.
/// </summary>
public static class FlareMaui
{
    /// <summary>Name of the <see cref="ActivitySource"/> the package's own spans use.</summary>
    public const string SourceName = "Flare.Maui";

    internal static readonly ActivitySource Source = new(SourceName);

    private static UnhandledExceptionReporter? _reporter;
    private static BaseProcessor<LogRecord>? _logProcessor;
    private static volatile bool _breadcrumbs;
    private static AppHangWatchdog? _hangs;
    private static HttpClient? _screenshotHttp;

    /// <summary>
    /// Set by the platform glue: captures the current page as JPEG bytes no larger than the limit, or null.
    /// Unset on the plain net10.0 core, where there is nothing to capture.
    /// </summary>
    internal static Func<int, CancellationToken, Task<byte[]?>>? ScreenshotCapture { get; set; }
    private static FlareMauiOptions? _options;
    private static FlareEnrichment _enrichment = new(sendDefaultPii: false);

    /// <summary>The tracer provider, or null before <c>UseFlare</c>.</summary>
    public static TracerProvider? Tracing { get; private set; }

    /// <summary>The meter provider, or null before <c>UseFlare</c> or when metrics are off.</summary>
    public static MeterProvider? Metering { get; private set; }

    /// <summary>This process's <c>session.id</c>.</summary>
    public static string? SessionId { get; private set; }

    /// <summary>Flush buffered spans and metrics, e.g. before the app is suspended.</summary>
    public static void Flush(int timeoutMilliseconds = 2000)
    {
        Tracing?.ForceFlush(timeoutMilliseconds);
        Metering?.ForceFlush(timeoutMilliseconds);
        _logProcessor?.ForceFlush(timeoutMilliseconds);
    }

    /// <summary>Record a breadcrumb for the session timeline. No-op when breadcrumbs are off or before <c>UseFlare</c>.</summary>
    public static void AddBreadcrumb(string category, string message)
    {
        if (_breadcrumbs) Breadcrumbs.Add(Source, category, message);
    }

    /// <summary>
    /// Attach a user to every span and log record from now on, as <c>user.id</c>. <paramref name="name"/> and
    /// <paramref name="email"/> are sent only when <see cref="FlareMauiOptions.SendDefaultPii"/> is on. Pass a null
    /// id (on sign-out) to clear the user. Release health counts users from <c>user.id</c>.
    /// </summary>
    public static void SetUser(string? id, string? name = null, string? email = null) => _enrichment.SetUser(id, name, email);

    /// <summary>Stamp <paramref name="key"/> = <paramref name="value"/> on every span and log record. A null or empty value removes it.</summary>
    public static void SetTag(string key, string? value) => _enrichment.SetTag(key, value);

    /// <summary>
    /// Stamp a group of related values as <c>name.key</c> attributes, e.g. <c>SetContext("cart", ...)</c> gives
    /// <c>cart.items</c>. Replaces the previous values of the same name; null clears them.
    /// </summary>
    public static void SetContext(string name, IReadOnlyDictionary<string, string>? values) => _enrichment.SetContext(name, values);

    /// <summary>Record an exception the app handled itself, so it shows on the Errors page.</summary>
    public static void RecordException(Exception exception) => _reporter?.Report(exception, fatal: false);

    internal static ResourceBuilder BuildResource(FlareMauiOptions options, FlareDeviceInfo device) =>
        ResourceBuilder.CreateEmpty()
            .AddService(options.ServiceName!, serviceVersion: options.ServiceVersion ?? device.AppVersion)
            .AddTelemetrySdk()
            .AddAttributes(device.ToResourceAttributes());

    internal static void ConfigureExporter(OtlpExporterOptions o, FlareMauiOptions options, string signal)
    {
        o.Endpoint = options.SignalUri(signal);
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
        if (!string.IsNullOrEmpty(options.IngestKey))
            o.Headers = $"Authorization=Bearer {options.IngestKey}";
    }

    /// <summary>
    /// The OpenTelemetry exporter reads its disk-retry switch from the environment when it is created,
    /// so this must run before any provider is built.
    /// </summary>
    internal static void EnableDiskRetry(string directory)
    {
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY", "disk");
        Environment.SetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_OTLP_DISK_RETRY_DIRECTORY_PATH", directory);
    }

    internal static string NewSessionId() => Guid.NewGuid().ToString("N");

    /// <summary>Build the providers. Called once from <c>UseFlare</c>; a second call is ignored.</summary>
    internal static void Initialize(FlareMauiOptions options, FlareDeviceInfo device, string queueDirectory)
    {
        options.Validate();
        if (Tracing is not null) return;

        if (options.EnableOfflineQueue)
        {
            OfflineQueue.Prune(queueDirectory, options.OfflineQueueMaxBytes, options.OfflineQueueMaxAge, DateTime.UtcNow);
            EnableDiskRetry(queueDirectory);
        }

        var resource = BuildResource(options, device);
        SessionId = NewSessionId();
        _breadcrumbs = options.Breadcrumbs;
        _options = options;
        _enrichment = new FlareEnrichment(options.SendDefaultPii);

        var tracing = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(SourceName)
            .AddProcessor(new SessionProcessor(SessionId))
            .AddProcessor(new EnrichmentProcessor(_enrichment));
        foreach (var s in options.AdditionalSources) tracing.AddSource(s);
        if (options.InstrumentHttpClient) tracing.AddHttpClientInstrumentation();
        if (options.ScrubAttribute is not null || options.BeforeSend is not null)
            tracing.AddProcessor(new ScrubProcessor(options.ScrubAttribute, options.BeforeSend));
        tracing.AddOtlpExporter(o => ConfigureExporter(o, options, "traces"));
        Tracing = tracing.Build();

        if (options.ExportMetrics)
        {
            var metrics = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource);
            foreach (var m in options.AdditionalMeters) metrics.AddMeter(m);
            if (options.InstrumentHttpClient) metrics.AddHttpClientInstrumentation();
            metrics.AddOtlpExporter(o => ConfigureExporter(o, options, "metrics"));
            Metering = metrics.Build();
        }

        if (options.CaptureUnhandledExceptions)
        {
            _reporter = new UnhandledExceptionReporter(Source, Flush, ScreenshotAfterReport(options), options.ScrubAttribute);
            _reporter.Attach();
        }
    }

    /// <summary>Starts (first call) or unpauses the UI-thread watchdog. A no-op when hang detection is off.</summary>
    internal static void ResumeHangDetection(Action<Action> postToMainThread)
    {
        if (_options is not { DetectAppHangs: true } o) return;
        _hangs ??= new AppHangWatchdog(Source, postToMainThread, o.AppHangThreshold, Flush);
        _hangs.Resume();
    }

    /// <summary>Pauses the watchdog while the app is backgrounded.</summary>
    internal static void PauseHangDetection() => _hangs?.Pause();

    private static Action<string, string, bool>? ScreenshotAfterReport(FlareMauiOptions options)
    {
        if (!options.CaptureScreenshotOnError || ScreenshotCapture is not { } capture) return null;
        _screenshotHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var reporter = new ScreenshotReporter(options, SessionId!, ct => capture(options.ScreenshotMaxBytes, ct), _screenshotHttp);
        return (traceId, spanId, fatal) => reporter.Report(traceId, spanId, fatal ? TimeSpan.FromSeconds(3) : null);
    }

    /// <summary>Stops exporting and releases the providers. For tests; apps leave telemetry on until exit.</summary>
    internal static void Shutdown()
    {
        _screenshotHttp?.Dispose();
        _screenshotHttp = null;
        _hangs?.Dispose();
        _hangs = null;
        _options = null;
        _reporter?.Detach();
        _reporter = null;
        Tracing?.Dispose();
        Metering?.Dispose();
        Tracing = null;
        Metering = null;
        SessionId = null;
        _enrichment = new FlareEnrichment(sendDefaultPii: false);
        _logProcessor = null;
        _breadcrumbs = false;
    }

    internal static void ConfigureLogging(OpenTelemetryLoggerOptions o, FlareMauiOptions options, FlareDeviceInfo device)
    {
        o.SetResourceBuilder(BuildResource(options, device));
        o.IncludeFormattedMessage = true;
        if (SessionId is not null) o.AddProcessor(new SessionLogProcessor(SessionId));

        o.AddProcessor(new EnrichmentLogProcessor(_enrichment));
        if (options.ScrubAttribute is { } scrub) o.AddProcessor(new ScrubLogProcessor(scrub));

        if (options.Breadcrumbs && options.BreadcrumbLogLevel != LogLevel.None)
            o.AddProcessor(new BreadcrumbLogProcessor(Source, options.BreadcrumbLogLevel));

        var exporterOptions = new OtlpExporterOptions();
        ConfigureExporter(exporterOptions, options, "logs");
        // Built here rather than through AddOtlpExporter so Flush can reach it when the app is backgrounded.
        _logProcessor = new BatchLogRecordExportProcessor(new OtlpLogExporter(exporterOptions));
        o.AddProcessor(_logProcessor);
    }
}
