# Known-good OTLP logger package versions

Pinned to what was actually run against a live Flare instance while
writing [`../how-to/run-standalone.md`](../how-to/run-standalone.md)
(2026-08-07). OTLP-for-logs support is a fairly new corner of each of
these ecosystems and some of it tracks pre-release OpenTelemetry SDK
versions — if something doesn't compile against a newer version you pick
up later, these are confirmed-working fallbacks.

| Package | Version |
|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.17.0 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.17.0 |
| `ZLogger` | 2.5.10 |
| `Serilog.Sinks.OpenTelemetry` | 4.2.0 |
| `NLog.Targets.OpenTelemetryProtocol` | 1.2.7 |