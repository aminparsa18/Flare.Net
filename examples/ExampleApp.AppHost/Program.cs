var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

// .WithReference(flare) injects ConnectionStrings__flare (Flare.Ingest's OTLP/gRPC endpoint) -
// ExampleApp.LogGenerator reads it via Aspire.Flare's builder.AddFlareOtlpExporter("flare")
// instead of the ambient OTEL_EXPORTER_OTLP_ENDPOINT env var WithOtlpEndpoint(flare) used to set.
builder.AddProject<Projects.ExampleApp_LogGenerator>("log-generator")
    .WithReference(flare)
    .WaitFor(flare);

builder.Build().Run();
