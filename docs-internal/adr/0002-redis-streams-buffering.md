# ADR-0002: Redis Streams for durable ingest buffering

Status: Accepted
Date: 2026-08-07

## Context

`Flare.Ingest` receives OTLP telemetry and must buffer it before a batched
insert into ClickHouse (batching by size/interval is itself a deliberate
design choice — see the ingestion pipeline description in
`docs/explanation/architecture.md` once written). The open question was
what that buffer is backed by, and specifically whether it needs to survive
`Flare.Ingest` restarting mid-buffer. For a self-hosted tool whose whole
value proposition includes running reliably with minimal operational
ceremony, silently dropping in-flight telemetry on every restart/deploy
was judged unacceptable.

## Decision

Buffer ingested events in **Redis, using Redis Streams**, from the start —
not introduced later as a durability upgrade. Wired via
`Aspire.Hosting.Redis` (`AddRedis(...).WithDataVolume().WithPersistence(...)`)
and the `Aspire.StackExchange.Redis` client. Consumers use
`XREADGROUP`/`XACK` consumer groups, giving at-least-once delivery into the
ClickHouse flush worker.

## Alternatives considered

- **In-memory ring buffer.** The original v1-simplicity plan. Rejected:
  events would not survive `Flare.Ingest` restarting mid-buffer — exactly
  the failure mode this decision exists to avoid — and there is no way to
  add durability to an in-memory structure without effectively becoming
  the Redis Streams design anyway.
- **`IDistributedCache` / ASP.NET Core `OutputCaching`.** Considered and
  rejected as a poor fit on inspection: both are value-cache / HTTP-response-
  cache abstractions, not append-only durable queue primitives — using
  either would have meant working against the abstraction's intended shape
  rather than with it.

## Consequences

- `Flare.Ingest` takes a hard runtime dependency on Redis being available;
  this is bundled into the standalone `docker-compose.yml` stack rather than
  being an optional component.
- The ClickHouse flush path is a Redis Streams consumer, not a plain
  in-process queue — any future ingest-side buffering change (e.g.
  multi-replica scaling) has to work within the consumer-group model rather
  than reinventing buffering from scratch.
- **Valkey** (`Aspire.Hosting.Valkey`, wire-compatible with Redis) was noted
  as a plausible later swap if Redis's license terms become a concern for a
  bundled `docker-compose` dependency. This was flagged, not decided — no
  ADR supersedes this one for that reason today.

## Related documentation

- `docs/explanation/architecture.md` (once created — Phase 3+ of the
  documentation migration)
- `db/clickhouse/README.md` — the flush-worker side of this pipeline
- `src/Flare.Ingest/README.md` — local dev loop for the ingest project