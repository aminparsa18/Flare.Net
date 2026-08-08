var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

// Ingest's OTLP endpoints are fixed/unproxied by design (4317 gRPC / 4318 HTTP) - any OTLP
// client points at them directly, identical to docs/getting-started.md's docker-compose
// story. FlareResource doesn't expose the ingest sub-resource, so this is set explicitly
// rather than via WithReference.
builder.AddProject<Projects.ExampleApp_LogGenerator>("log-generator")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")
    .WaitFor(flare);

builder.Build().Run();
