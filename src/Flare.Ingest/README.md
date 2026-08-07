# Flare.Ingest

The OTLP ingestion boundary for [Flare](../../Planning.md). This is where every logger
(Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`, or any other OTLP-speaking
source, .NET or not) actually lands.

## What it does today

This project implements v1's first roadmap item: **"OTLP logs receiver (gRPC + HTTP)"**.
Concretely, it:

1. Terminates the OpenTelemetry Protocol for logs on two ports, matching OTLP's
   conventional defaults:
   - **`4317`** — gRPC (`opentelemetry.proto.collector.logs.v1.LogsService/Export`)
   - **`4318`** — HTTP, `POST /v1/logs`, content-negotiating both
     `application/x-protobuf` and `application/json` bodies
2. Maps parsed OTLP log records into a minimal internal [`LogEvent`](Model/LogEvent.cs)
   via [`OtlpLogMapper`](Otlp/OtlpLogMapper.cs) — shared by both transports, so a log
   sent over gRPC and the same log sent over HTTP/JSON produce identical output.
3. Hands each `LogEvent` to an [`ILogEventSink`](Sinks/ILogEventSink.cs). The only
   implementation today is [`ConsoleLogEventSink`](Sinks/ConsoleLogEventSink.cs) — it
   just logs a line per event so ingestion is visible during local dev.

## What it deliberately does *not* do (yet)

Per [Planning.md](../../Planning.md)'s roadmap, the following are **separate, later
items** — don't extend this project to cover them without checking there first:

- No persistence. ClickHouse now exists as an `Aspire.AppHost` resource with a real
  schema (see [`db/clickhouse/`](../../db/clickhouse/)), but nothing in `Flare.Ingest`
  writes to it yet — `ConsoleLogEventSink` is still a throwaway placeholder. The batched
  ClickHouse insert pipeline replaces its DI registration
  (`AddSingleton<ILogEventSink, ...>()` in `Program.cs`), nothing else needs to change.
- No real buffering/batching (no ring buffer, no flush-by-size-or-interval logic).
- No `Flare.Api` or `Flare.Dashboard` wiring.

## Project layout

```
Protos/       Vendored official OTLP .proto files - see Protos/VENDORED.md for why
              they're vendored instead of a NuGet dependency, and how to re-vendor.
Model/        LogEvent - the internal, transport-agnostic representation.
Otlp/         OtlpLogMapper (OTLP -> LogEvent), the gRPC service, the HTTP endpoint.
Sinks/        ILogEventSink and its placeholder ConsoleLogEventSink.
```

## Running it

Via the Aspire AppHost (recommended - wires up the `otlp-grpc`/`otlp-http` endpoints
and the standard Aspire dashboard):

```bash
dotnet run --project ../Flare.AppHost
```

Or standalone:

```bash
dotnet run --project .
```

## Smoke-testing manually

**HTTP + JSON** (easiest - no OTel SDK needed, just `curl`):

```bash
curl -s -X POST http://localhost:4318/v1/logs \
  -H "Content-Type: application/json" \
  -d '{"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"curl-test"}}]},"scopeLogs":[{"scope":{"name":"manual-test"},"logRecords":[{"timeUnixNano":"1700000000000000000","severityNumber":9,"severityText":"INFO","body":{"stringValue":"hello from curl"}}]}]}]}'
```

Expect `200 {}` and a matching line in the Flare.Ingest console output.

**gRPC** and **HTTP + protobuf**: point the OpenTelemetry .NET SDK's OTLP exporter at
`http://localhost:4317` (`OtlpExportProtocol.Grpc`) or
`http://localhost:4318/v1/logs` (`OtlpExportProtocol.HttpProtobuf`) respectively, emit
a log via `ILogger`, and confirm it shows up in the console sink. There's no JSON
protocol option in the SDK's exporter, which is why the JSON path above uses `curl`
instead.

## Tests

`../Flare.Ingest.Tests` covers `OtlpLogMapper` (severity/timestamp handling, all
`AnyValue` attribute variants, trace/span id hex encoding) with plain unit tests - no
hosting or network needed. Run with `dotnet test`.
