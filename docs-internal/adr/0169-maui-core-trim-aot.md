# ADR-0169: Trim and Native AOT compatibility of the Flare.Maui core

Status: Accepted

Date: 2026-10-09

## Context

[ADR-0166](0166-maui-sdk.md) left the trimming/AOT behaviour of `Flare.Maui` untested. Release MAUI
builds on iOS are trimmed and Native AOT, on Android trimmed, so a reflection-based surprise would only show
up in a store build. The plain `net10.0` target carries the MAUI-free core (options, resource attributes,
`SessionProcessor`, `UnhandledExceptionReporter`, `FlareMaui` bootstrap), and that is what could be checked
without the MAUI workloads or a device.

## Decision

- **Verified with a throwaway console app** that compiles the core's sources directly (so internals are
  reachable), builds the same tracer, meter and logger providers as `UseFlare` (OpenTelemetry 1.19.1,
  OTLP exporter, `Instrumentation.Http` 1.19.0) and publishes both ways on osx-x64 with the trim, AOT and
  single-file analyzers on:
  - `PublishAot=true`: **0 IL2xxx/IL3xxx warnings**, a native Mach-O binary.
  - `PublishTrimmed=true` (self-contained): **0 warnings**.
- **Run against a local OTLP/HTTP listener**, both binaries sent `/v1/traces`, `/v1/metrics` and
  `/v1/logs` with the `Authorization: Bearer` header; the span carried `session.id`, the metric and log
  reached the listener, and `RecordException` produced an `app.unhandled_exception` span. In the AOT
  binary an uncaught exception on a thread flushed its `app.unhandled_exception` span before the process
  died (the fatal path).
- **No code change was needed**, and no OpenTelemetry-package warnings appeared to record. The core keeps to
  that: no reflection, no `System.Text.Json` serialization, no `Type.GetType`.
- **`IsTrimmable` and `IsAotCompatible` are set on the plain `net10.0` target only**, so a future change that
  introduces a trim/AOT warning fails the build. The mobile targets are left out: they need the MAUI
  workloads to build here, and their trimming is governed by the MAUI SDK.

## Not decided here

The `Platform/` glue (`UseFlare`, Shell navigation tracing) is not covered: it uses MAUI APIs that
could only be checked in a real trimmed Android/iOS build, which waits for device testing. The disk-retry
exporter was enabled in the run but no failed export was forced, so retry-from-disk under AOT is unverified.

## Consequences

The core can be shipped to trimmed and AOT apps without `TrimmerRootAssembly` or rd.xml entries. A new
dependency on the core must be annotated for trimming or the build breaks.
