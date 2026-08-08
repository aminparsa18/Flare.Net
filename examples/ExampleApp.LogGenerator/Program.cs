using ExampleApp.LogGenerator;

var builder = WebApplication.CreateBuilder(args);

// Same Flare.ServiceDefaults every project in Flare's own solution uses - AddServiceDefaults()
// wires OpenTelemetry logging/tracing/metrics and only activates the OTLP exporter when
// OTEL_EXPORTER_OTLP_ENDPOINT is set (see that project's Extensions.cs). This project has
// zero Flare-specific code - ExampleApp.AppHost is what points it at Flare's ingest endpoint.
builder.AddServiceDefaults();

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
