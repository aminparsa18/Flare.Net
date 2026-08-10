# Flare.Aspire

> Package ID `Flare.Aspire`, not `Aspire.Flare` - that prefix is reserved on nuget.org for
> Microsoft's own official integrations. Same project (`src/Aspire.Flare`), same
> `builder.AddFlareOtlpExporter(...)` API - only the published package name differs.

.NET Aspire client integration for [Flare](https://github.com/aminparsa18/Flare.Net) - a
self-hosted, OpenTelemetry-native log dashboard for .NET developers.

Points your app's OTLP log exporter at Flare.Ingest, reading the connection info injected by
`.WithReference(flare)` on the AppHost side (see [`Flare.Hosting.Aspire`](https://www.nuget.org/packages/Flare.Hosting.Aspire)):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFlareOtlpExporter("flare");

var app = builder.Build();
app.Run();
```

```csharp
// AppHost
var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApi>("myapi")
    .WithReference(flare); // injects ConnectionStrings__flare -> Flare.Ingest's OTLP/gRPC endpoint
```

`AddFlareOtlpExporter` registers a **named** OTLP log exporter, additive alongside whatever
exporter your app's own OpenTelemetry setup already registered (e.g. the Aspire dashboard
collector via `ConfigureOpenTelemetry()`/`UseOtlpExporter()`) - it doesn't replace it. Today
ingestion is pure OTLP with no auth, so this package's job is small: read the connection
string and point an exporter at it. It exists mainly as a **forward-compatible seam** - once
Flare's "Auth + multi-user / roles" roadmap item lands and ingest needs an API key/token, this
is the natural place to attach it to the exporter, the same job `Aspire.Seq`'s client package
does today for Seq's own API key.

Logs and traces - Flare.Ingest doesn't receive OTLP metrics yet (a separate, later roadmap item with a materially different data model than traces).

## Status

Pre-alpha, mirroring [`Flare.Hosting.Aspire`](https://github.com/aminparsa18/Flare.Net/blob/main/src/Aspire.Hosting.Flare/README.md)'s
status.
