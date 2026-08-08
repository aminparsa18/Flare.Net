using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Standard Aspire ServiceDefaults boilerplate: OTel instrumentation for this app itself
// (not to be confused with Flare.Ingest's job of *receiving* OTLP from other apps),
// health checks, and service discovery. Not a design decision for the Flare.Ingest
// roadmap item - this is what every Aspire-orchestrated project gets.
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            // Signal-specific AddOtlpExporter() (unnamed - still reads the standard
            // OTEL_EXPORTER_OTLP_* env vars exactly like the cross-cutting UseOtlpExporter()
            // this replaced), not the cross-cutting UseOtlpExporter(). The OTel SDK explicitly
            // forbids mixing UseOtlpExporter with any signal-specific AddOtlpExporter call on
            // the same IServiceCollection (throws NotSupportedException at startup) - and
            // Aspire.Flare's AddFlareOtlpExporter needs to register its own additional, named
            // signal-specific log exporter alongside this one, so this one has to be
            // signal-specific too. Confirmed against the real installed
            // OpenTelemetry.Exporter.OpenTelemetryProtocol 1.17.0 assembly (reflection, not just
            // XML docs - those over-documented overloads this SDK version doesn't actually ship).
            builder.Services.AddOpenTelemetry()
                .WithLogging(logging => logging.AddOtlpExporter())
                .WithMetrics(metrics => metrics.AddOtlpExporter())
                .WithTracing(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Unlike the stock Aspire template (which gates these behind IsDevelopment()),
        // Flare ships as self-hosted software distributed via docker-compose - health
        // checks need to work in every environment, not just local dev, since that's
        // what Docker healthcheck/k8s liveness probes (and Aspire's own
        // WithHttpHealthCheck orchestration) depend on.
        app.MapHealthChecks("/health");

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        return app;
    }
}