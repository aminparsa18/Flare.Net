# Flare.Ingest

The OTLP ingestion boundary for [Flare](../../Planning.md). This is where every logger
(Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`, or any other OTLP-speaking
source, .NET or not) actually lands.

## What it does today

This project implements v1's first three roadmap items: **"OTLP logs receiver (gRPC +
HTTP)"**, **"Internal log-event model + ClickHouse schema"**, and **"Batched insert
pipeline"**. Concretely, it:

1. Terminates the OpenTelemetry Protocol for logs on two ports, matching OTLP's
   conventional defaults:
   - **`4317`** — gRPC (`opentelemetry.proto.collector.logs.v1.LogsService/Export`)
   - **`4318`** — HTTP, `POST /v1/logs`, content-negotiating both
     `application/x-protobuf` and `application/json` bodies
2. Maps parsed OTLP log records into a minimal internal [`LogEvent`](Model/LogEvent.cs)
   via [`OtlpLogMapper`](Otlp/OtlpLogMapper.cs) — shared by both transports, so a log
   sent over gRPC and the same log sent over HTTP/JSON produce identical output.
3. Hands each `LogEvent` to an [`ILogEventSink`](Sinks/ILogEventSink.cs), implemented by
   [`RedisStreamLogEventSink`](Sinks/RedisStreamLogEventSink.cs), which buffers it into a
   Redis Stream (`XADD`) — durably, so events survive `Flare.Ingest` restarting before
   they're flushed (`Planning.md`'s "Buffering layer" decision, 2026-08-07: Redis
   Streams over an in-memory ring buffer).
4. [`ClickHouseFlushWorker`](Pipeline/ClickHouseFlushWorker.cs), a `BackgroundService`,
   reads that stream via a consumer group, accumulates a batch, and flushes it into
   ClickHouse (see [`db/clickhouse/`](../../db/clickhouse/)) once
   `LogEventPipeline:BatchSize` or `LogEventPipeline:FlushInterval` is reached — only
   acknowledging entries after a successful insert (at-least-once delivery; see the
   class doc comment for the crash-recovery / poison-message details).

## What it deliberately does *not* do (yet)

Per [Planning.md](../../Planning.md)'s roadmap, the following are **separate, later
items** — don't extend this project to cover them without checking there first:

- No `Flare.Api` or `Flare.Dashboard` wiring.
- No dead-letter subsystem — poison stream entries (exceeding
  `LogEventPipeline:MaxDeliveryAttempts`) are logged and dropped, not routed anywhere
  for inspection.

## Project layout

```
Protos/       Vendored official OTLP .proto files - see Protos/VENDORED.md for why
              they're vendored instead of a NuGet dependency, and how to re-vendor.
Model/        LogEvent - the internal, transport-agnostic representation.
Otlp/         OtlpLogMapper (OTLP -> LogEvent), the gRPC service, the HTTP endpoint.
Sinks/        ILogEventSink and its RedisStreamLogEventSink implementation.
Pipeline/     LogEventPipelineOptions, the Redis<->LogEvent JSON wire format
              (LogEventJsonContext), the ClickHouse row mapper/writer, and
              ClickHouseFlushWorker (the consumer group read + batch-flush loop).
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

Expect `200 {}`. The event doesn't land in ClickHouse immediately - it's buffered in
Redis until `ClickHouseFlushWorker` flushes a batch (by default, every
`LogEventPipeline:FlushInterval`, currently 2s, or once `BatchSize` events accumulate).
Confirm it landed via `db/clickhouse/README.md`'s `curl`-based query example
(`SELECT * FROM logs ORDER BY Timestamp DESC LIMIT 1`).

**gRPC** and **HTTP + protobuf**: point the OpenTelemetry .NET SDK's OTLP exporter at
`http://localhost:4317` (`OtlpExportProtocol.Grpc`) or
`http://localhost:4318/v1/logs` (`OtlpExportProtocol.HttpProtobuf`) respectively, emit
a log via `ILogger`, and confirm it lands in ClickHouse the same way. There's no JSON
protocol option in the SDK's exporter, which is why the JSON path above uses `curl`
instead.

## Tests

`../Flare.Ingest.Tests` covers, all with plain xUnit unit tests - no hosting, no
network, no containers:

- `OtlpLogMapper` (severity/timestamp handling, all `AnyValue` attribute variants,
  trace/span id hex encoding).
- `ClickHouseRowMapper` (the `LogEvent` → ClickHouse row mapping: empty-string
  coalescing, `ObservedTimestamp` fallback, column order).
- `LogEventJsonContext` (the Redis Stream wire format round-trips correctly).

Real Redis/ClickHouse I/O (`RedisStreamLogEventSink`, `ClickHouseFlushWorker`,
`ClickHouseLogEventWriter`) is deliberately **not** unit-tested against a fake - the
`IDatabase` surface is too large to hand-fake meaningfully with no mocking framework in
this repo. It's covered by real end-to-end runs instead (see "Smoke-testing manually"
above, plus a restart-mid-buffer check to exercise the durability the Redis Streams
buffer exists for).

Run with `dotnet test`.
