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
    .WithReference(flare); // injects ConnectionStrings__flare -> Flare.Ingest's OTLP/gRPC endpoint

builder.Build().Run();
```

`AddFlare` wires up ClickHouse (log storage), Redis (the batched-insert buffer), the OTLP
ingest receiver, the query API, and the dashboard SPA - pulling Flare's published Docker Hub
images (`xracer007/flare-ingest`, `xracer007/flare-api`, `xracer007/flare-dashboard`) rather
than building from source, the same way [`docker-compose.yml`](https://github.com/aminparsa18/Flare.Net/blob/main/docker-compose.yml)
in Flare's own repo does.

Pair `.WithReference(flare)` above with the [`Flare.Aspire`](https://www.nuget.org/packages/Flare.Aspire)
client package's `builder.AddFlareOtlpExporter("flare")` in the consuming project - it reads
the injected `ConnectionStrings__flare` and registers an OTLP log exporter pointed at it. Or
skip the client package and call `WithOtlpEndpoint(flare)` instead of `WithReference(flare)`
here, which sets `OTEL_EXPORTER_OTLP_ENDPOINT` directly on the consuming resource (`useHttp: true`
for the OTLP/HTTP endpoint, `:4318`, instead of gRPC) - for wiring your own `OpenTelemetry` SDK
call by hand.

## Status

Pre-alpha, `imageTag` defaults to `"edge"` - Flare has no stable release yet. See the
[getting-started docs](https://github.com/aminparsa18/Flare.Net/blob/main/docs/getting-started.md)
and [Aspire hosting docs](https://github.com/aminparsa18/Flare.Net/blob/main/docs/aspire-hosting.md)
for the full API and a snippet per logger.
