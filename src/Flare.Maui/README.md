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
`session.id` on every span and `ILogger` log record (logs also flush when the app goes to the background), `HttpClient` spans with `traceparent` propagation, a `navigation` span per Shell
navigation, `app.unhandled_exception` spans for `AppDomain` and unobserved-task exceptions, and a flush when the
app goes to the background. Design: [ADR-0166](../../docs-internal/adr/0166-maui-sdk.md). Setup, ingest-key
advice and limits: [Send telemetry from a MAUI app](../../docs/how-to/send-maui-telemetry.md).

Breadcrumbs (lifecycle, taps, pages, log lines; [ADR-0172](../../docs-internal/adr/0172-maui-breadcrumbs.md)) appear on the
session timeline; `o.IncludeTextInBreadcrumbs` / `o.IncludeTitleInBreadcrumbs` are off by default (PII), and
`FlareMaui.AddBreadcrumb(category, message)` adds your own.

Hangs: a watchdog posts a ping to the UI thread and reports an `app.hang` span (error status, backdated to when
the thread stopped answering) if it is blocked for `AppHangThreshold` (default 2 s, minimum 500 ms). It is paused in
the background; `DetectAppHangs = false` turns it off. It does not capture the blocked thread's stack
([ADR-0173](../../docs-internal/adr/0173-maui-app-hang-detection.md)).

Error screenshots are opt-in (`o.CaptureScreenshotOnError = true`): the current page is captured when an exception is
reported, uploaded to `/v1/screenshots` and shown on the session timeline. A screenshot can contain anything on screen
([ADR-0174](../../docs-internal/adr/0174-maui-error-screenshots.md)).

Your own spans and metrics: add the source/meter name to `o.AdditionalSources` / `o.AdditionalMeters`.
`FlareMaui.RecordException(ex)` reports an exception you handled. `FlareMaui.Tracing` and `.Metering` expose the
providers.

Native crashes (a SIGSEGV, an Android ANR or low-memory kill, an iOS watchdog termination) are reported on the next
launch from Android `ApplicationExitInfo` (Android 11+) and iOS MetricKit (iOS 14+), as `app.unhandled_exception`
spans in the earlier session so release health counts them. `o.CaptureNativeCrashes = false` turns it off. It keeps a
small journal of the last ten launches in `flare/runs.json` under the app data directory.

Not yet: Windows.

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
