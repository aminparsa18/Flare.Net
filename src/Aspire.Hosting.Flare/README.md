# Flare.Hosting.Aspire

> Package ID `Flare.Hosting.Aspire`, not `Aspire.Hosting.Flare` - that prefix is reserved on
> nuget.org for Microsoft's own official integrations. Same project (`src/Aspire.Hosting.Flare`),
> same `builder.AddFlare(...)` API - only the published package name differs.

.NET Aspire hosting integration for [Flare](https://github.com/aminparsa18/Flare.Net) - a
self-hosted, OpenTelemetry-native log dashboard for .NET developers.

Adds the whole Flare stack to your own AppHost as a dev-time resource:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApi>("myapi")
    .WithOtlpEndpoint(flare); // OTEL_EXPORTER_OTLP_ENDPOINT -> Flare.Ingest's OTLP/gRPC endpoint

builder.Build().Run();
```

`AddFlare` wires up ClickHouse (log storage), Redis (the batched-insert buffer), the OTLP
ingest receiver, the query API, and the dashboard SPA - pulling Flare's published Docker Hub
images (`xracer007/flare-ingest`, `xracer007/flare-api`, `xracer007/flare-dashboard`) rather
than building from source, the same way [`docker-compose.yml`](https://github.com/aminparsa18/Flare.Net/blob/main/docker-compose.yml)
in Flare's own repo does.

`WithOtlpEndpoint(flare)` points your app's OTLP exporter at Flare.Ingest, resolved correctly
per execution context (loopback locally, container-network alias under compose, real Service
DNS/ingress once published) instead of a hardcoded `http://localhost:4317`. Pass
`useHttp: true` for the OTLP/HTTP endpoint (`:4318`) instead of gRPC.

`.WithReference(flare)` also works, for code that wants the raw connection info instead - it
injects `ConnectionStrings__flare` with the same OTLP/gRPC URL. That's what the future
`Flare.Aspire` client package reads.

## Status

Pre-alpha. `imageTag` defaults to `"edge"` - Flare has no stable release yet. See the
[getting-started docs](https://github.com/aminparsa18/Flare.Net/blob/main/docs/getting-started.md)
for a snippet per logger.
