using ExampleApp.LogGenerator;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Same Flare.ServiceDefaults every project in Flare's own solution uses - AddServiceDefaults()
// wires OpenTelemetry tracing/metrics instrumentation, health checks, and service discovery
// (see that project's Extensions.cs). No OTEL_EXPORTER_OTLP_ENDPOINT is set by
// ExampleApp.AppHost anymore, so ConfigureOpenTelemetry's own ambient OTLP exporter stays off -
// AddFlareOtlpExporter below is what actually ships logs to Flare.
builder.AddServiceDefaults();

// Flare.Aspire (project src/Aspire.Flare/): reads ConnectionStrings__flare (injected by .WithReference(flare) on the
// AppHost side) and registers named OTLP log and trace exporters pointed at Flare.Ingest -
// additive alongside whatever ConfigureOpenTelemetry() already wired, same mechanism
// Aspire.Seq's AddSeqEndpoint uses for Seq. This is the only Flare-specific line in this whole
// project.
builder.AddFlareOtlpExporter("flare");

// RandomLogGeneratorWorker.ActivitySource isn't picked up by ConfigureOpenTelemetry()'s own
// AddAspNetCoreInstrumentation()/AddHttpClientInstrumentation() (those only auto-instrument
// framework-level spans) - a custom ActivitySource needs an explicit AddSource() call before the
// SDK will sample/export anything it creates. This is what makes GenerateBurst's per-log child
// spans (nested under the ASP.NET Core-instrumented POST /generate-burst request span) actually
// real, exported spans, not just no-op Activity objects. Same story for
// RandomLogGeneratorWorker.Meter (Planning.md's v6 Pass 4) - AddMeter() is the metrics
// pipeline's equivalent of AddSource(), needed before the SDK samples/exports anything this
// custom Meter's Counter/Histogram/ObservableGauge record.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(RandomLogGeneratorWorker.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(RandomLogGeneratorWorker.MeterName));

builder.Services.AddSingleton<RandomLogGeneratorWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RandomLogGeneratorWorker>());

var app = builder.Build();

app.MapDefaultEndpoints(); // /health, /alive

app.MapPost("/generate-burst", (int count, RandomLogGeneratorWorker worker) =>
{
    var clamped = Math.Clamp(count, 1, 500);
    worker.GenerateBurst(clamped);
    return Results.Ok(new { generated = clamped });
});

app.Run();
