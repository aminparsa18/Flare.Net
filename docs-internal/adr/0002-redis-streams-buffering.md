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
  the failure mode this decision exists to avoid. Durability options for an
  in-memory structure exist (a local WAL, an embedded SQLite-backed queue,
  etc.) but adding persistence, crash recovery, acknowledgement, consumer
  coordination, and replay on top of a ring buffer means building and
  operating a bespoke durable queue — the same "wrap, don't reinvent"
  argument this project applies to ClickHouse (ADR-0013). Redis Streams
  already is that durable queue, tested and operated by someone else.
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
- **Redis moves from "likely disposable/cache-like" to load-bearing stateful
  infrastructure.** Anyone flushing or resetting the Redis volume (e.g.
  during troubleshooting) is discarding unflushed telemetry, not just
  warming a cache — and that means "durable" here is bounded by Redis's own
  persistence config, not absolute. Redis is configured for RDB snapshotting
  (`WithPersistence(interval: 30s, keysChangedThreshold: 100)` in
  `Flare.AppHost/AppHost.cs`, mirrored by `--save 30 100` in
  `docker-compose.yml`), not AOF. An event that has been
  streamed into Redis and acknowledged to the OTLP sender is not yet fsynced
  — if Redis crashes before the next snapshot, up to ~30s (or 100 key
  changes) of buffered-but-unflushed events can be lost. The guarantee is
  therefore: **at-least-once delivery from Redis to ClickHouse, subject to
  Redis's configured snapshot interval** — not zero-loss.
- **Duplicate delivery is possible, and nothing downstream collapses it
  today.** `ClickHouseFlushWorker` only `XACK`s a stream entry after its
  batch insert succeeds, so a crash between the ClickHouse insert and the
  `XACK` causes that entry to be redelivered and re-inserted on restart. The
  `logs` table (`db/clickhouse/0001_logs.sql`) is a plain `MergeTree`, not
  `ReplacingMergeTree` — unlike `alert_rules`/`alert_events`, which do use
  `ReplacingMergeTree` for their own upsert needs — so a redelivered event
  lands as a genuine duplicate row, not a deduped one. `EventId`
  (`db/clickhouse/0002_logs_event_id.sql`) exists as a keyset-pagination
  tiebreaker, not a dedup key, and query-time dedup on it is not implemented.
  Downstream consumers of log data should currently tolerate occasional
  duplicate rows around ingest-side crashes/restarts.

## Related documentation

- `docs/explanation/architecture.md` (once created — Phase 3+ of the
  documentation migration)
- `db/clickhouse/README.md` — the flush-worker side of this pipeline
- `src/Flare.Ingest/README.md` — local dev loop for the ingest project