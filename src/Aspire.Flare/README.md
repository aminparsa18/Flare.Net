# Aspire.Flare

.NET Aspire client integration for [Flare](https://github.com/aminparsa18/Flare.Net) - a
self-hosted, OpenTelemetry-native log dashboard for .NET developers.

Points your app's OTLP log exporter at Flare.Ingest, reading the connection info injected by
`.WithReference(flare)` on the AppHost side (see [`Aspire.Hosting.Flare`](https://www.nuget.org/packages/Aspire.Hosting.Flare)):

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

Logs only for now - Flare.Ingest doesn't receive traces or metrics yet (a separate roadmap item).

## Status

Pre-alpha, mirroring [`Aspire.Hosting.Flare`](https://github.com/aminparsa18/Flare.Net/blob/main/src/Aspire.Hosting.Flare/README.md)'s
status. Not yet packed/pushed to nuget.org.
