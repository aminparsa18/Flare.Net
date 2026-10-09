# ADR-0166: Flare.Maui client package

Status: Accepted

Date: 2026-10-09

## Context

A .NET MAUI app can already send OTLP with stock OpenTelemetry packages (the
[MAUI how-to](../../docs/how-to/send-maui-telemetry.md)), but each team has to rediscover the same
workarounds: no generic host, so `AddOpenTelemetry()` never starts; gRPC is unreliable on iOS and Android;
exports are lost offline; crashes and backgrounding need explicit flushes. Phase 2 packages those
workarounds.

## Decision

- **A thin configuration package, not a wrapper SDK.** `Flare.Maui` references `OpenTelemetry`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` and `OpenTelemetry.Instrumentation.Http` and builds ordinary
  `TracerProvider`/`MeterProvider`/`ILogger` pipelines. It adds no exporter, protocol or span model of its own, so
  everything the OpenTelemetry docs say about processors, sources and samplers still applies, and Flare's server
  needs nothing new. `FlareMaui.Tracing`/`Metering` expose the providers for anything the package does not cover.
- **`builder.UseFlare(o => ...)` on `MauiAppBuilder`.** Required: `Endpoint` (the base URL of the OTLP/HTTP
  receiver, port 4318) and `ServiceName`. `IngestKey` is optional. The package appends `/v1/{traces,logs,metrics}`
  and always uses `HttpProtobuf`. The exporter has no compression option in 1.19, so exports are uncompressed.
- **Offline queue by delegating to the OpenTelemetry exporter's disk retry** (`OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=disk`
  with a directory under `FileSystem.CacheDirectory`) rather than writing our own queue. It is experimental in the
  SDK, so `EnableOfflineQueue` can be turned off and the setting is isolated in one method. The directory is
  per-process-start safe (the SDK owns its files). Telemetry still dropped by the OS killing the app between
  batches is not recoverable.
- **Resource attributes** are `service.name`, `service.version` (`AppInfo.VersionString`), `app.build`,
  `os.type`, `os.version`, `device.manufacturer` and `device.model.identifier`. No device or advertising
  identifier is collected: privacy defaults are no PII and no stable device id. `session.id` is a random GUID per
  process start, stamped on every span; it is not persisted, so a relaunch is a new session.
- **Unhandled exceptions** from `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are
  recorded as an `app.unhandled_exception` span with an exception event (the shape `/errors` already groups) and
  flushed synchronously with a 2-second cap before the process dies.
- **Flush on background** through MAUI lifecycle events (Android `OnStop`, iOS/Mac Catalyst `DidEnterBackground`).
- **Automatic spans:** `HttpClient` through the stock instrumentation (so `traceparent` propagates), and Shell
  navigation as a `navigation` span carrying `screen.name`. Non-Shell navigation is not instrumented.
- **Layout.** Everything that does not touch MAUI types (options, resource attributes, session processor,
  exception reporter, bootstrap) compiles for plain `net10.0` so it is unit-tested; the MAUI glue is compiled only
  for the mobile target frameworks. The project is **not** in `Flare.slnx` because restoring it needs the MAUI
  workloads, which the Linux CI job does not install; it has its own `Flare.Maui.slnx`.
- **Not in this phase:** native crash reports delivered on the next launch (Android `ApplicationExitInfo`, iOS
  MetricKit) and Windows. Both stay on the roadmap.

## Alternatives considered

- **A custom on-disk queue.** Rejected for now: it duplicates retry, ordering and file-corruption handling the
  exporter already has, and an experimental dependency is easier to replace than our own persistence code.
- **Wrapping OpenTelemetry behind a Flare-specific API** (`Flare.Track(...)`). Rejected: it would freeze a second
  API surface and block access to the ecosystem's instrumentation.
- **Reusing `AddOpenTelemetry()` on `builder.Services`.** Rejected: MAUI has no host, so the hosted service that
  starts the providers never runs.
