# Flare.Maui

.NET MAUI client for [Flare](https://github.com/aminparsa18/Flare.Net). One call sends traces, logs and
metrics over OTLP/HTTP, retries from disk after a restart, and reports unhandled exceptions.

```csharp
var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>()
       .UseFlare(o =>
       {
           o.Endpoint = new Uri("https://flare.example.com:4318");
           o.ServiceName = "my-maui-app";
           o.IngestKey = "<key pinned to my-maui-app>"; // ships in the binary: treat as public
       });
```

What it does: `service.*`, `os.*`, `device.*` and `app.build` resource attributes (no device id), a per-launch
`session.id` on every span, `HttpClient` spans with `traceparent` propagation, a `navigation` span per Shell
navigation, `app.unhandled_exception` spans for `AppDomain` and unobserved-task exceptions, and a flush when the
app goes to the background. Design: [ADR-0166](../../docs-internal/adr/0166-maui-sdk.md). Setup, ingest-key
advice and limits: [Send telemetry from a MAUI app](../../docs/how-to/send-maui-telemetry.md).

Your own spans and metrics: add the source/meter name to `o.AdditionalSources` / `o.AdditionalMeters`.
`FlareMaui.RecordException(ex)` reports an exception you handled. `FlareMaui.Tracing` and `.Metering` expose the
providers.

Not yet: native crash reports on next launch, Windows.

## Build

Needs the MAUI workloads (`dotnet workload install maui`), so it is not in `Flare.slnx`:

```bash
dotnet build Flare.Maui.slnx
dotnet test src/Flare.Maui.Tests
```

## Tests

`Flare.Maui.Tests` covers the MAUI-free core on plain `net10.0`: option validation, endpoint building, resource
attributes, the session processor, the exception reporter, and an end-to-end provider run against an in-process
HTTP listener. The `UseFlare` glue (lifecycle events, Shell hook) needs a device or emulator and is verified by
running an app.
