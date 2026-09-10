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
    .WithReference(flare) // injects ConnectionStrings__flare -> Flare.Ingest's OTLP/gRPC endpoint
    .WaitForFlare(flare);

builder.Build().Run();
```

`AddFlare` wires up ClickHouse (log storage), Redis (the batched-insert buffer), the OTLP
ingest receiver, the query API, the alert-rule evaluation worker, and the dashboard SPA -
pulling Flare's published Docker Hub images (`xracer007/flare-ingest`, `xracer007/flare-api`,
`xracer007/flare-alert-worker`, `xracer007/flare-dashboard`) rather than building from source,
the same way [`docker-compose.yml`](https://github.com/aminparsa18/Flare.Net/blob/main/docker-compose.yml)
in Flare's own repo does.

> **No published `xracer007/flare-alert-worker` image exists yet.** `AddFlare` unconditionally
> adds an alert-worker container as of this package version, but until a Flare release actually
> publishes that image (see [ADR-0018](https://github.com/aminparsa18/Flare.Net/blob/main/docs-internal/adr/0018-alert-worker-extraction.md)'s
> release gate), the pull for it will fail. Don't bump past this package version's `imageTag`
> default until that's resolved.

Pair `.WithReference(flare)` above with the [`Flare.Aspire`](https://www.nuget.org/packages/Flare.Aspire)
client package's `builder.AddFlareOtlpExporter("flare")` in the consuming project - it reads
the injected `ConnectionStrings__flare` and registers an OTLP log exporter pointed at it. Or
skip the client package and call `WithOtlpEndpoint(flare)` instead of `WithReference(flare)`
here, which sets `OTEL_EXPORTER_OTLP_ENDPOINT` directly on the consuming resource (`useHttp: true`
for the OTLP/HTTP endpoint, `:4318`, instead of gRPC) - for wiring your own `OpenTelemetry` SDK
call by hand.

## Status

Pre-alpha. `imageTag` defaults to the latest stable Flare release this package version
was tested against (currently `"0.5.0"`) - pass `imageTag: "edge"` to track Flare's
unreleased `main` branch instead. See the
[getting-started tutorial](https://github.com/aminparsa18/Flare.Net/blob/main/docs/tutorials/getting-started.md)
and [Aspire hosting how-to](https://github.com/aminparsa18/Flare.Net/blob/main/docs/how-to/run-with-aspire.md)
for the full API and a snippet per logger.
