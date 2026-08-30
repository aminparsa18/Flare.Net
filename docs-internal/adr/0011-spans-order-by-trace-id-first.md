# ADR-0011: `spans` sorts by `(TraceId, StartTime, SpanId)`, not service-first

Status: Accepted
Date: 2026-08-10 (v4, `0007_spans.sql`)

## Context

Same shape of problem as `logs`' `ORDER BY` (ADR-0010), a different
resolution: `spans`' access patterns genuinely conflict too. "Get a full
trace by id" (the trace-detail waterfall view) wants `TraceId` leading its
physical sort order. "List/search spans by service and time" (a
logs-explorer-style view over spans) wants `ServiceName`+time leading
instead — the same shape `logs` itself uses.

## Decision

`ORDER BY (TraceId, StartTime, SpanId)` — trace locality first.

## Alternatives considered

- **`(ServiceName, StartTime, ...)`, matching `logs`' own convention.**
  Not chosen: this migration picks trace locality instead, since the
  waterfall view is the feature this roadmap slice was actually built
  for. The two patterns weren't judged equally important for the feature
  being shipped.

## Consequences

- **Known, deliberately unresolved trade-off, same spirit as `logs`' own
  one**: service+time span search (a hot path for a hypothetical
  logs-explorer-style spans view) doesn't benefit from the `ORDER BY`
  prefix. Covered adequately for v1 by the `idx_service` skip index (span
  rows within one `TraceId` group are naturally low-cardinality in
  `ServiceName`) plus monthly partition pruning — not by the sort order
  itself. Named follow-up if service+time span search becomes a hot path:
  a `StartTime`-first projection, addable non-destructively later, same
  mechanism as `logs`' own named follow-up.
- **This choice is what makes `optimize_skip_unused_shards` possible in
  cluster mode** (see [ADR-0003](0003-distributed-tables-plain-names-and-sharding.md)):
  because rows physically cluster by `TraceId` first, `SpanQueryService.GetTraceAsync`
  can prune shards that provably don't hold a given trace's spans. A
  service-first ordering would not have supported that optimization the
  same way.
- No synthetic id column exists for `spans` (unlike `logs.EventId`) — a
  direct consequence of trace-id-first ordering combined with OTel's own
  guarantee: `(TraceId, SpanId)` is spec-guaranteed present and unique on
  every span, so it already serves as both natural key and pagination
  tiebreaker without needing a Flare-internal identifier the way `logs`
  needed `EventId`.

## Related documentation

- `db/clickhouse/README.md` — `0007_spans.sql` and the `Events`
  Nested-column/`StatusCode Enum8` decisions alongside this one
- ADR-0003 — the cluster-mode sharding decision this `ORDER BY` choice
  enables
- ADR-0010 — the equivalent, differently-resolved trade-off for `logs`