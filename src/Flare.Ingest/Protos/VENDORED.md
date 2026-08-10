# Vendored: OpenTelemetry Protocol (OTLP) proto definitions

These `.proto` files are vendored unmodified from the official
[open-telemetry/opentelemetry-proto](https://github.com/open-telemetry/opentelemetry-proto) repository.

- **Tag:** `v1.11.0`
- **Commit:** `790608c4d51e6ffc12210b541e8514cbed9e91a4`
- **Vendored:** 2026-08-07 (logs); trace files added 2026-08-10 at the same tag; metrics
  files added 2026-08-10 (same day, v6) at the same tag again, so wire compatibility
  stays pinned to one snapshot across all three signals.
- **License:** Apache-2.0 (see each file's header)

Files:
```
opentelemetry/proto/common/v1/common.proto
opentelemetry/proto/resource/v1/resource.proto
opentelemetry/proto/logs/v1/logs.proto
opentelemetry/proto/collector/logs/v1/logs_service.proto
opentelemetry/proto/trace/v1/trace.proto
opentelemetry/proto/collector/trace/v1/trace_service.proto
opentelemetry/proto/metrics/v1/metrics.proto
opentelemetry/proto/collector/metrics/v1/metrics_service.proto
```

## Why vendored, not a NuGet dependency

There is no official Microsoft/OpenTelemetry NuGet package that ships generated C# OTLP
message types. The official `OpenTelemetry.Exporter.OpenTelemetryProtocol` package (the
client/exporter side) dropped `Google.Protobuf`/`Grpc.Tools` in favor of a hand-written,
write-only wire serializer — it has no reusable public message classes or gRPC service
contracts, since it only ever needs to *emit* OTLP, never receive or deserialize it.

Vendoring the proto files directly and compiling them via `Grpc.Tools`/`Google.Protobuf`
is what Microsoft's own OTLP receiver — the **Aspire Dashboard** (`dotnet/aspire`,
`src/Aspire.Dashboard/Otlp/opentelemetry/proto/...`) — does for exactly the same problem.

Flare.Ingest's OTLP wire compatibility depends only on this vendored tag, not on any
client-side OTel package version (see Planning.md's note on client package version churn).

## Re-vendoring

Bump the tag deliberately and update this file — do not let re-vendoring happen as an
incidental side effect of an unrelated change.

```bash
TAG=vX.Y.Z
for f in opentelemetry/proto/common/v1/common.proto \
         opentelemetry/proto/resource/v1/resource.proto \
         opentelemetry/proto/logs/v1/logs.proto \
         opentelemetry/proto/collector/logs/v1/logs_service.proto \
         opentelemetry/proto/trace/v1/trace.proto \
         opentelemetry/proto/collector/trace/v1/trace_service.proto \
         opentelemetry/proto/metrics/v1/metrics.proto \
         opentelemetry/proto/collector/metrics/v1/metrics_service.proto; do
  curl -sf -o "$f" "https://raw.githubusercontent.com/open-telemetry/opentelemetry-proto/$TAG/$f"
done
```