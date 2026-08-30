# ADR-0007: Log pattern clustering computed at ingest flush time, not query time

Status: Accepted
Date: 2026-08-17 (v16, Log pattern detection / Drain clustering)

## Context

Building a feature to cluster similar log message bodies (`"GET
/api/orders/123"` / `"GET /api/orders/456"` → `"GET /api/orders/<*>"`) and
surface them ranked by occurrence count (the Logs page's Patterns modal)
required deciding *when* that clustering computation happens: at query
time (cheap to build, no schema change) or once at ingest time, storing a
`PatternId` per row (real `GROUP BY` aggregates over arbitrary time
ranges, at the cost of a schema/pipeline change).

## Decision

Compute pattern clustering **at ClickHouse-flush time**, inside
`ClickHouseFlushWorker.FlushAsync`, right before the batch write — not in
`OtlpLogMapper` or the OTLP gRPC/HTTP receive path itself (that would add
latency to the ingestion request), and not at query time. `PatternId`/
`PatternTemplate` are stored per row (migration `0010_logs_pattern.sql`),
and `Flare.Api`'s `POST /api/logs/patterns` does a plain
`GROUP BY PatternId` — the same shape the existing volume-histogram/
service-breakdown aggregates already use.

## Alternatives considered

- **Compute clusters at query time**, scanning `Body` text for every
  matching row on every page load (or sampling/capping to stay fast).
  Rejected on a direct efficiency comparison, not a "less work" one: the
  flagship stat the feature promises ("12,481 occurrences" over a wide
  window) needs either a full scan of `Body` text for every matching row
  on every request, or silently sampling and breaking the promised *exact*
  count. Ingest-time pays the clustering cost once, inside work that's
  already CPU-bound and already happening (the flush worker), turning the
  read side into a plain `GROUP BY` — no query-time scan at all.

## Consequences

- `LogPatternOptions.Enabled` (default `true`) is an instant, config-only
  rollback valve if the flush-time clustering cost ever becomes a problem.
- Pattern clustering state (`DrainPatternMatcher`'s cluster tree) is
  in-memory per `Flare.Ingest` process by default — this created a
  cross-replica fragmentation problem once Flare ran with more than one
  ingest replica (two replicas independently clustering whatever subset of
  logs they each happened to consume), fixed separately by making cluster
  storage pluggable behind `IPatternClusterStore` with an opt-in
  Redis-backed shared store — see
  [`docs/explanation/clustering.md`](../../docs/explanation/clustering.md#drain-log-pattern-clustering-across-replicas).
- No duration/p95 shipped in v1 pattern cards (occurrence count, error
  count, first/last seen only) — logs have no duration field anywhere in
  the schema, and nothing in the codebase joins `logs`↔`spans` for
  aggregates. Confirmed with the user before implementation, scoped down
  from the original pitch deliberately, not an oversight; a duration
  metric is a named Later item requiring a `TraceId`/`SpanId` join, and
  would only cover logs that carry trace context anyway.
- No skip index was added for `PatternId` in v1 — a `GROUP BY` doesn't
  benefit from one, and the one path that could (drilling into a single
  pattern's rows) already inherits the standard bounded-time-range query
  path. No backfill of historical rows either (same precedent as
  `EventId`'s own migration, `0002_logs_event_id.sql`) — unbackfilled rows
  read back as `PatternId=''` and are simply excluded from the ranked
  list, not shown as a misleading "unknown" bucket.

## Related documentation

- `docs/explanation/clustering.md` — the shared-storage follow-up this
  decision led to
- `db/clickhouse/README.md` — migration `0010_logs_pattern.sql`