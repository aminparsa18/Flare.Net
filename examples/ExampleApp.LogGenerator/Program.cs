using ExampleApp.LogGenerator;

var builder = WebApplication.CreateBuilder(args);

// Same Flare.ServiceDefaults every project in Flare's own solution uses - AddServiceDefaults()
// wires OpenTelemetry tracing/metrics instrumentation, health checks, and service discovery
// (see that project's Extensions.cs). No OTEL_EXPORTER_OTLP_ENDPOINT is set by
// ExampleApp.AppHost anymore, so ConfigureOpenTelemetry's own ambient OTLP exporter stays off -
// AddFlareOtlpExporter below is what actually ships logs to Flare.
builder.AddServiceDefaults();

// Flare.Aspire (project src/Aspire.Flare/): reads ConnectionStrings__flare (injected by .WithReference(flare) on the
// AppHost side) and registers a named OTLP log exporter pointed at Flare.Ingest - additive
// alongside whatever ConfigureOpenTelemetry() already wired, same mechanism Aspire.Seq's
// AddSeqEndpoint uses for Seq. This is the only Flare-specific line in this whole project.
builder.AddFlareOtlpExporter("flare");

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
