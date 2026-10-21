using Flare.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Same namespace convention as Flare.Aspire and Flare.ServiceDefaults: referencing the package
// makes the extension method discoverable without an extra using.
namespace Microsoft.Extensions.Hosting;

/// <summary>Sends a .NET app's logs, traces and metrics to <see href="https://github.com/aminparsa18/Flare.Net">Flare</see> in one call.</summary>
public static class FlareOpenTelemetryExtensions
{
    // Named exporters are additive: they never displace, or are displaced by, an unnamed exporter
    // the app already configured (for example through Flare.ServiceDefaults reading
    // OTEL_EXPORTER_OTLP_ENDPOINT), which would otherwise share one options instance with last-wins.
    // Each signal needs its own name for the same reason: with one shared name the logs and traces
    // exporters picked up the metrics signal path.
    private const string LogsExporterName = "flare-logs";
    private const string TracesExporterName = "flare-traces";
    private const string MetricsExporterName = "flare-metrics";

    /// <summary>
    /// Sends logs, traces and metrics to the Flare.Ingest at <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="builder">The host builder, for example <c>WebApplication.CreateBuilder(args)</c>.</param>
    /// <param name="endpoint">Flare.Ingest's OTLP endpoint, such as <c>http://localhost:4317</c>.</param>
    /// <param name="apiKey">Ingest key, only needed when the instance requires one.</param>
    public static IHostApplicationBuilder AddFlareTelemetry(
        this IHostApplicationBuilder builder,
        string endpoint,
        string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        return builder.AddFlareTelemetry(o =>
        {
            o.Endpoint = new Uri(endpoint);
            if (apiKey is not null)
            {
                o.ApiKey = apiKey;
            }
        });
    }

    /// <summary>
    /// Sends logs, traces and metrics to Flare, reading <see cref="FlareOptions"/> from the
    /// <c>Flare</c> configuration section and then from <paramref name="configure"/>. With neither,
    /// it exports to <see cref="FlareOptions.DefaultEndpoint"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="FlareOptions.TraceSampleRatio"/> is outside 0 to 1.</exception>
    public static IHostApplicationBuilder AddFlareTelemetry(
        this IHostApplicationBuilder builder,
        Action<FlareOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new FlareOptions();
        builder.Configuration.GetSection(FlareOptions.SectionName).Bind(options);
        configure?.Invoke(options);
        Validate(options);

        var otel = builder.Services.AddOpenTelemetry();

        if (options.ServiceName is not null || options.ServiceVersion is not null)
        {
            otel.ConfigureResource(r => r.AddService(
                options.ServiceName ?? builder.Environment.ApplicationName,
                serviceVersion: options.ServiceVersion));
        }

        otel.WithLogging(
            logging => logging.AddOtlpExporter(LogsExporterName, o => ConfigureExporter(o, options, "v1/logs")),
            logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

        otel.WithTracing(tracing =>
        {
            if (options.InstrumentAspNetCore)
            {
                tracing.AddAspNetCoreInstrumentation();
            }

            if (options.InstrumentHttpClient)
            {
                tracing.AddHttpClientInstrumentation();
            }

            if (options.ActivitySources.Count > 0)
            {
                tracing.AddSource([.. options.ActivitySources]);
            }

            if (CreateSampler(options.TraceSampleRatio) is { } sampler)
            {
                tracing.SetSampler(sampler);
            }

            tracing.AddOtlpExporter(TracesExporterName, o => ConfigureExporter(o, options, "v1/traces"));
        });

        otel.WithMetrics(metrics =>
        {
            if (options.InstrumentAspNetCore)
            {
                metrics.AddAspNetCoreInstrumentation();
            }

            if (options.InstrumentHttpClient)
            {
                metrics.AddHttpClientInstrumentation();
            }

            if (options.InstrumentRuntime)
            {
                metrics.AddRuntimeInstrumentation();
            }

            if (options.Meters.Count > 0)
            {
                metrics.AddMeter([.. options.Meters]);
            }

            metrics.AddOtlpExporter(MetricsExporterName, o => ConfigureExporter(o, options, "v1/metrics"));
        });

        return builder;
    }

    internal static void Validate(FlareOptions options)
    {
        if (double.IsNaN(options.TraceSampleRatio) || options.TraceSampleRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.TraceSampleRatio,
                "Flare.TraceSampleRatio must be between 0 and 1.");
        }
    }

    // Null at 1.0 so the SDK keeps its own default (parent-based, always on) rather than a
    // ratio sampler that would only ever answer yes.
    internal static Sampler? CreateSampler(double ratio) =>
        ratio >= 1.0 ? null : new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));

    // With an endpoint set in code the SDK uses it as given and does not append the signal path,
    // so for OTLP/HTTP it has to be added here or every export would hit the root URL.
    internal static Uri ResolveEndpoint(FlareOptions options, string signalPath)
    {
        var endpoint = options.Endpoint ?? FlareOptions.DefaultEndpoint;
        if (options.Protocol != OtlpExportProtocol.HttpProtobuf)
        {
            return endpoint;
        }

        var baseUri = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/");
        return new Uri(baseUri, signalPath);
    }

    private static void ConfigureExporter(OtlpExporterOptions otlp, FlareOptions options, string signalPath)
    {
        otlp.Endpoint = ResolveEndpoint(options, signalPath);
        otlp.Protocol = options.Protocol;

        // Same header Flare.Aspire and Flare.Maui send; unset means no header at all, matching an
        // ingest instance that does not require a key.
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            otlp.Headers = $"Authorization=Bearer {options.ApiKey}";
        }
    }
}
