# Flare.Aspire

> Package ID `Flare.Aspire`, not `Aspire.Flare` - that prefix is reserved on nuget.org for
> Microsoft's own official integrations. Same project (`src/Aspire.Flare`), same
> `builder.AddFlareOtlpExporter(...)` API - only the published package name differs.

.NET Aspire client integration for [Flare](https://github.com/aminparsa18/Flare.Net) - a
self-hosted, OpenTelemetry-native log dashboard for .NET developers.

Points your app's OTLP log, trace and metric exporters at Flare.Ingest, reading the connection
string injected by `.WithReference(flare)` on the AppHost side (see
[`CommunityToolkit.Aspire.Hosting.Flare`](https://www.nuget.org/packages/CommunityToolkit.Aspire.Hosting.Flare),
which needs Aspire 17; the deprecated `Flare.Hosting.Aspire` 0.5.1 injects the same connection string):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFlareOtlpExporter("flare");

var app = builder.Build();
app.Run();
```

```csharp
// AppHost
var flare = builder.AddFlare("flare", builder.AddClickHouse("clickhouse"), builder.AddRedis("redis"));

builder.AddDotnetProject("myapi", "../MyApi/MyApi.csproj")
    .WithReference(flare); // injects ConnectionStrings__flare -> Flare.Ingest's OTLP/gRPC endpoint
```

`AddFlareOtlpExporter` registers **named** OTLP exporters, additive alongside whatever exporter
your app's own OpenTelemetry setup already registered (e.g. the Aspire dashboard collector), so
the Aspire dashboard keeps its telemetry. Your service defaults must then use the signal-specific
`AddOtlpExporter()` calls rather than `UseOtlpExporter()`; the OpenTelemetry SDK throws at
startup if both are registered. Set `Aspire:Flare:ApiKey` (or `configureSettings`) when ingest
requires a key.

Only need Flare, not the Aspire dashboard as well? Skip this package: call
`.WithOtlpExporter(flare)` on the AppHost side and the standard service defaults export to Flare.

Not using Aspire? [`Flare.OpenTelemetry`](https://www.nuget.org/packages/Flare.OpenTelemetry) does the same with a plain
endpoint and API key (`builder.AddFlareTelemetry("http://localhost:4317")`) and also sets up ASP.NET Core / `HttpClient` /
runtime instrumentation and an optional trace sampler.

## Status

Pre-1.0. Planned to be folded into `Flare.OpenTelemetry`, which will then read the same
connection string (see the repo's ADR-0281).
