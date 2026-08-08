using Aspire.Flare;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;

// Put extensions in the Microsoft.Extensions.Hosting namespace to ease discovery - referencing
// the package automatically adds this namespace, same convention Aspire.Seq's AspireSeqExtensions
// and this repo's own Flare.ServiceDefaults/Extensions.cs both use.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for connecting a project's OpenTelemetry log events to
/// <see href="https://github.com/aminparsa18/Flare.Net">Flare</see>'s OTLP ingest receiver.
/// </summary>
public static class AspireFlareExtensions
{
    /// <summary>
    /// Registers a second, named OTLP log exporter pointed at Flare.Ingest, additive alongside
    /// whatever exporter <c>ConfigureOpenTelemetry()</c> already registered (e.g. the Aspire
    /// dashboard collector) - the same signal-specific, named <c>AddOtlpExporter(name, ...)</c>
    /// mechanism <c>Aspire.Seq</c>'s <c>AddSeqEndpoint</c> uses to send telemetry to Seq without
    /// displacing other destinations. Logs only: Flare.Ingest doesn't receive traces or metrics
    /// yet (a separate "Later" roadmap item).
    /// <para>
    /// Requires <c>Flare.ServiceDefaults.ConfigureOpenTelemetry()</c> (or any other OTel setup
    /// this is layered on top of) to register its own exporter via the signal-specific
    /// <c>AddOtlpExporter</c> family too, not the cross-cutting <c>UseOtlpExporter()</c> - the
    /// OTel SDK throws <see cref="NotSupportedException"/> at startup if a signal-specific
    /// <c>AddOtlpExporter</c> call and <c>UseOtlpExporter()</c> land in the same
    /// <see cref="IServiceCollection"/>. Confirmed against the actual installed
    /// <c>OpenTelemetry.Exporter.OpenTelemetryProtocol</c> 1.17.0 assembly (via reflection, not
    /// just its XML docs - this version doesn't ship the named/multi-destination
    /// <c>UseOtlpExporter</c> overloads its own doc comments describe).
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to read config from and add services to.</param>
    /// <param name="connectionName">
    /// A name used to retrieve the connection string from the <c>ConnectionStrings</c>
    /// configuration section - the same name passed to <c>.WithReference(flare)</c> /
    /// <c>builder.AddFlare(connectionName)</c> on the AppHost side.
    /// </param>
    /// <param name="configureSettings">
    /// An optional delegate for customizing <see cref="FlareSettings"/>. Invoked after settings
    /// are read from configuration and the connection string, so it can override either.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No endpoint could be resolved from configuration, the connection string, or
    /// <paramref name="configureSettings"/>.
    /// </exception>
    public static void AddFlareOtlpExporter(
        this IHostApplicationBuilder builder,
        string connectionName = "flare",
        Action<FlareSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        var settings = new FlareSettings();
        builder.Configuration.GetSection("Aspire:Flare").Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is { } connectionString)
        {
            settings.Endpoint = new Uri(connectionString);
        }

        configureSettings?.Invoke(settings);

        if (settings.Endpoint is null)
        {
            throw new InvalidOperationException(
                $"No Flare endpoint configured. Ensure a connection string named '{connectionName}' is set " +
                "(e.g. via .WithReference(flare) on the AppHost side) or set FlareSettings.Endpoint explicitly " +
                "via the configureSettings delegate.");
        }

        builder.Services.AddOpenTelemetry().WithLogging(logging =>
            logging.AddOtlpExporter(connectionName, otlp =>
            {
                otlp.Endpoint = settings.Endpoint;
                otlp.Protocol = settings.Protocol;
            }));
    }
}
