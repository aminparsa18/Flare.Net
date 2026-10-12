# ADR-0150: Service allowlist on ingest keys

Status: Accepted

Date: 2026-10-08

## Context

[ADR-0149](0149-ingest-key-origin-allowlist.md) limits where a browser key can be used
from, but a leaked key can still write telemetry under any `service.name`, including a
backend service's, polluting its logs, traces and alerts.

## Decision

- **Per-key `AllowedServices`** (Identity migration Sqlite `0031` / Postgres `0006`,
  newline-separated, NULL = any). Names are trimmed and de-duplicated, compared
  case-sensitively. Set with `PUT /api/ingest-keys/{id}/services` (Admin, audited); an
  empty list lifts it.
- **Enforcement** in each OTLP receiver (logs, traces, metrics, profiles; HTTP and gRPC)
  through `IngestKeyScope`, after parsing and before any sink write. If any resource in
  the export has a missing or unlisted `service.name`, the whole request is refused: HTTP
  `403`, gRPC `PERMISSION_DENIED`, counted as `service-not-allowed` on the Ingestion
  page. Refusing rather than dropping the offending resources keeps a misconfigured
  exporter visible. Exporters treat the response as non-retryable.
- The allowlist reaches the receivers on the `IngestKeyUsageFeature` the middleware
  already sets per request, so the static key and auth-off setups are unaffected.

## Consequences

Combined with ADR-0149, a browser key can be limited to chosen origins and services.
It still cannot stop a forged `Origin` from a non-browser client writing to the
allowed services; per-key limits (ADR-0051) cap the volume. Changes apply within the
key cache refresh interval (30 s). Editing in the dashboard and CLI is not built yet.
