# ADR-0155: OTLP forwarding to other endpoints

Status: Accepted

Date: 2026-10-08

## Context

Flare had no way to copy ingested telemetry elsewhere, which blocks migrating to or from another
backend (run both side by side) and feeding a second consumer such as a SIEM.

## Decision

- **Forward at the receiver, after the request is accepted.** The six OTLP receivers (logs, traces,
  metrics over gRPC and HTTP) hand the parsed export to `IOtlpForwarder`. Forwarding sees exactly what
  was admitted: rejected requests (ingest-key limits, service allowlist, malformed) are never copied.
  Profiles are not forwarded (OTLP profiles is alpha, ADR-0141).
- **Config-driven, no migration.** Targets live in the `Forwarding:Targets` section (environment or
  appsettings). Each has a name, an OTLP/HTTP base `Endpoint`, optional `Headers`, and filters:
  `Signals`, `Services` (by `service.name`; resources of other services are stripped from the copy) and
  `IngestKeyIds` (only requests authenticated with those keys). Empty filter = everything. Config was
  chosen over a stored table and UI for this phase; a managed UI can follow without changing the sender.
- **Protobuf over OTLP/HTTP only.** Bodies are re-serialised as `application/x-protobuf`, gzip by default,
  to `{Endpoint}/v1/{logs|traces|metrics}`, whatever protocol the app used to reach Flare.
- **Best effort, never back-pressure ingest.** Each target has a bounded in-memory queue and one sender.
  A full queue drops the newest request and logs a warning; delivery retries network errors, 429 and 5xx
  with exponential backoff (`MaxAttempts`, default 3), and other 4xx are dropped without retry. The primary
  copy is already in Redis, so a slow or dead destination can never cost data in Flare itself. The
  trade-off is that the forwarded copy is at-most-once and lost on an ingest restart.

## Alternatives considered

- **Run an OpenTelemetry Collector fan-out in front of Flare.** Works today and stays the answer when
  durable forwarding is needed; this feature covers the case with no extra moving part.
- **Forward from the Redis stream with a consumer group.** Durable and replayable, but it forwards the
  mapped `LogEvent` rows, not OTLP, and loses fidelity (resource/scope structure, metric types).

## Consequences

- Ingest-key filtering relies on the key being resolved by `IngestApiKeyValidationMiddleware`; the static
  key and auth-off mode have no key id, so a target with `IngestKeyIds` receives nothing from them.
- The archive half of the roadmap item (Parquet/NDJSON to S3-compatible storage) is separate and remains.
