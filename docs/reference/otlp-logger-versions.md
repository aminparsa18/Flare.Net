# Known-good OTLP logger package versions

Pinned to what was actually run against a live Flare instance while
writing [`../how-to/deploy/run-standalone.md`](../how-to/deploy/run-standalone.md)
(2026-08-07). OTLP-for-logs support is a fairly new corner of each of
these ecosystems and some of it tracks pre-release OpenTelemetry SDK
versions — if something doesn't compile against a newer version you pick
up later, these are confirmed-working fallbacks.

| Package | Version |
|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.20.1 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.20.1 |
| `ZLogger` | 2.5.10 |
| `Serilog.Sinks.OpenTelemetry` | 4.2.0 |
| `NLog.Targets.OpenTelemetryProtocol` | 1.2.7 |

The two OpenTelemetry rows were re-verified on 1.20.1 on 2026-10-22: logs, traces
and metrics sent from a .NET app over both gRPC (4317) and HTTP/protobuf (4318)
all arrived in Flare. That SDK no longer follows HTTP redirects, so keep
[redirects off the OTLP port](../how-to/deploy/serve-under-a-sub-path.md).
