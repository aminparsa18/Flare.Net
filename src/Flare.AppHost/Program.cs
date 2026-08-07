var builder = DistributedApplication.CreateBuilder(args);

// Flare.Ingest: terminates OTLP over gRPC (4317) and HTTP (4318, protobuf + JSON).
// Both ports are fixed and unproxied so external OTLP clients (any logger's OTLP
// exporter) can point at them directly using the conventional OTLP port numbers,
// rather than Aspire's dashboard dev-proxy / dynamically-assigned ports.
var ingest = builder.AddProject<Projects.Flare_Ingest>("ingest")
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "http", name: "otlp-grpc", isProxied: false)
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "http", name: "otlp-http", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "otlp-http");

builder.Build().Run();