# Flare.OpenTelemetry

One-line OpenTelemetry setup for sending a .NET app's logs, traces and metrics to
[Flare](https://github.com/aminparsa18/Flare.Net), a self-hosted OpenTelemetry-native observability platform.
Plain OpenTelemetry underneath: no agent, no proprietary format. Works with or without .NET Aspire.

```sh
dotnet add package Flare.OpenTelemetry
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFlareTelemetry("http://localhost:4317");
```

That registers the OTLP exporter for all three signals, ASP.NET Core and `HttpClient` instrumentation, and .NET runtime
metrics. Everything else is optional:

```csharp
builder.AddFlareTelemetry(o =>
{
    o.Endpoint = new Uri("https://flare.example.com:4318");
    o.Protocol = OtlpExportProtocol.HttpProtobuf; // signal paths (/v1/traces ...) are added for you
    o.ApiKey = "<ingest key>";                    // only if the instance requires one
    o.ServiceName = "orders";
    o.TraceSampleRatio = 0.1;                     // keep 10% of new traces; joined traces follow their root
    o.ActivitySources.Add("MyApp.Orders");        // your own spans
    o.Meters.Add("MyApp.Orders");                 // your own metrics
});
```

The same settings bind from the `Flare` configuration section (`Flare__Endpoint`, `Flare__ApiKey`,
`Flare__TraceSampleRatio`, ...), so a deployment can configure them with environment variables and the call stays
`builder.AddFlareTelemetry()`. With no endpoint at all it exports to `http://localhost:4317`.

The exporter is registered under its own names (`flare-logs`, `flare-traces`, `flare-metrics`), so it is additive: it never replaces an exporter your app already
has, such as the Aspire dashboard's. Inside an Aspire AppHost, prefer
[`Flare.Aspire`](https://www.nuget.org/packages/Flare.Aspire), which reads the address `.WithReference(flare)` injects.

Full guide: [docs/how-to/instrument/send-dotnet-telemetry.md](https://github.com/aminparsa18/Flare.Net/blob/main/docs/how-to/instrument/send-dotnet-telemetry.md).

## Status

Pre-alpha, like the other Flare packages.
