using System.Diagnostics;
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
    }

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

        if (options.EnableOfflineQueue) EnableDiskRetry(queueDirectory);

        var resource = BuildResource(options, device);
        SessionId = NewSessionId();

        var tracing = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(SourceName)
            .AddProcessor(new SessionProcessor(SessionId));
        foreach (var s in options.AdditionalSources) tracing.AddSource(s);
        if (options.InstrumentHttpClient) tracing.AddHttpClientInstrumentation();
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
            _reporter = new UnhandledExceptionReporter(Source, Flush);
            _reporter.Attach();
        }
    }

    /// <summary>Stops exporting and releases the providers. For tests; apps leave telemetry on until exit.</summary>
    internal static void Shutdown()
    {
        _reporter?.Detach();
        _reporter = null;
        Tracing?.Dispose();
        Metering?.Dispose();
        Tracing = null;
        Metering = null;
        SessionId = null;
    }

    internal static void ConfigureLogging(OpenTelemetryLoggerOptions o, FlareMauiOptions options, FlareDeviceInfo device)
    {
        o.SetResourceBuilder(BuildResource(options, device));
        o.IncludeFormattedMessage = true;
        o.AddOtlpExporter(e => ConfigureExporter(e, options, "logs"));
    }
}
