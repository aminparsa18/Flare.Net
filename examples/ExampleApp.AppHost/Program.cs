var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

// .WithReference(flare) injects ConnectionStrings__flare (Flare.Ingest's OTLP/gRPC endpoint) -
// ExampleApp.LogGenerator reads it via Flare.Aspire's builder.AddFlareOtlpExporter("flare")
// instead of the ambient OTEL_EXPORTER_OTLP_ENDPOINT env var WithOtlpEndpoint(flare) used to set.
//
// .WaitFor(flare) would NOT actually wait - flare is a lifetime-less grouping node and Aspire
// unconditionally skips those as WaitFor targets (see WaitForFlare's doc comment). WaitForFlare
// waits on the real dashboard resource instead, which transitively depends on everything else.
builder.AddProject<Projects.ExampleApp_LogGenerator>("log-generator")
    .WithReference(flare)
    .WaitForFlare(flare);

builder.Build().Run();
