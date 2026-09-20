# ADR-0030: Pre-aggregate Services-tab RED metrics at flush time via a materialized view

Status: Accepted

Date: 2026-09-20

## Context

`docs-internal/planning/roadmap.md`'s "Service RED metrics pre-aggregated
at flush time, not queried live" item flagged that the Traces page's
Services tab Table view (`ServiceOverviewQueryBuilder`/
`ServiceOverviewQueryService`) runs a `GROUP BY ServiceName` live over the
raw `spans` table on every load - and the tab polls every 10 seconds while
open. No index helps this query (see `ServiceOverviewQueryBuilder`'s own
remarks: a bloom-filter skip index only prunes an *equality* filter, and
this query is deliberately unfiltered-by-service), so it's a bounded-by-
partition scan of the window's `StartTime` range on every single poll.
The roadmap item's own reasoning pointed at an already-shipped precedent:
ADR-0007 moved Drain log-pattern clustering from query time to ClickHouse-
flush time for exactly this reason - an aggregate recomputed on every page
load should instead be computed once, when the data is already being
processed.

## Decision

**A ClickHouse materialized view (`service_metrics_mv`) attached to
`spans`, writing pre-aggregated per-`(ServiceName, one-minute TimeBucket)`
rows into a new `AggregatingMergeTree` table (`service_metrics`,
`db/clickhouse/0022_service_metrics.sql`)** - not a second explicit write
from `Flare.Ingest`'s `SpanFlushWorker`. Concretely:

- The view fires once per block ClickHouse actually writes to `spans` -
  i.e. as part of the exact same `InsertBinaryAsync` call
  `ClickHouseSpanWriter`/`SpanFlushWorker` already make, no second write
  path to keep in sync and no risk of double-counting across retried or
  overlapping flush batches (a manual second query scoped by a time range
  would have exactly that risk, since flush batches aren't guaranteed
  non-overlapping in `StartTime`).
- `RequestCount`/`ErrorCount` are plain `SimpleAggregateFunction(sum,
  UInt64)` columns - trivially mergeable. The percentile columns
  (`P50State`/`P95State`/`P99State`) are `AggregateFunction(quantile(p),
  UInt64)` state blobs produced by `quantileState()` in the view and
  combined back into a real percentile via `quantileMerge()` at query time
  (`Flare.Api`'s new `ServiceMetricsQueryBuilder`) - ClickHouse's own
  mechanism for merging partial quantile estimates across rows, buckets,
  and (in cluster mode) shards.
- `ServiceOverviewQueryService.GetOverviewAsync` reads `service_metrics`
  only when the request carries no resource-attribute filter chips (and
  `ServiceMetricsOptions.Enabled` is true); any filter chip present falls
  back to the existing live `ServiceOverviewQueryBuilder` query over
  `spans`, unchanged. The Services tab's filter chips
  (`ResourceAttributeFilter`) are arbitrary free-text key/value pairs
  (confirmed against `ResourceAttributeFiltersRow.svelte`/
  `ResourceAttributeFilterSqlBuilder`), and `service_metrics` has no
  dimension for them - only `ServiceName` + a one-minute `TimeBucket`.
  Storing the full `ResourceAttributes` map per aggregate row would
  reintroduce the per-row cardinality this feature exists to avoid.
- `service_metrics.TimeBucket` is minute-truncated
  (`toStartOfMinute(StartTime)`), but a caller's window bounds generally
  aren't. `ServiceMetricsQueryBuilder` floors the window start and
  ceilings the window end to whole minutes before binding, rather than
  comparing the truncated `TimeBucket` against non-aligned instants - the
  latter would silently *drop* up to a minute of genuinely in-window data
  at the start of the range. Flooring/ceiling instead means the
  pre-aggregated path can only ever slightly *over*-include at the edges -
  see Consequences.
- `ServiceMetricsOptions.Enabled` (default `true`) is an instant,
  config-only rollback valve - same shape as ADR-0007's
  `LogPatternOptions.Enabled` and `QueryCacheOptions.Enabled` (ADR-0029):
  `false` makes `ServiceOverviewQueryService` always take the live path,
  as if a filter chip were always present, no redeploy or migration
  rollback needed.
- `ClickHouseSpanWriter` sets `materialized_views_ignore_errors` on its
  `InsertBinaryAsync` call. ClickHouse's default behavior is to fail the
  *entire* originating insert if a dependent materialized view's own
  insert fails - without this setting, a bug in `service_metrics_mv`
  would take down span ingestion itself, not just the RED-metrics rollup.
  Confirmed via a live spike before shipping, same practice
  `ClickHouseSpanRowMapper`'s own remarks already document for the
  `Events` Nested-column round-trip.

Scope is deliberately limited to the Table view. The Map view's
dependency graph (`ServiceDependencyQueryBuilder` - a self-join keyed by
the `peer.service`-overridden "effective service", producing both nodes
*and* edges) and the per-node call breakdown
(`ServiceCallBreakdownQueryBuilder` - grouped by unbounded-cardinality
`peer.service`/`db.system`+`db.operation`) are materially different
aggregation shapes; pre-aggregating them is a bigger, separate effort.

## Alternatives considered

- **`SpanFlushWorker` computes aggregates itself and issues a second
  explicit write**, matching the roadmap item's literal "populated by
  SpanFlushWorker" phrasing. Rejected on inspection: request/error counts
  are easy (a plain in-memory `GroupBy` over the batch it already holds),
  but percentiles aren't summable across batches - correctly merging
  quantile estimates across flush batches and time buckets needs
  ClickHouse's own `quantileState`/`quantileMerge` machinery, which a
  hand-rolled C# aggregation would have to reimplement (a t-digest
  estimator, plus a bespoke serialization format for the partial state)
  just to avoid a materialized view. Even setting percentiles aside, a
  second query scoped by an explicit time range risks double-counting
  rows across retried/overlapping flush batches in a way the trigger-on-
  insert materialized view structurally cannot.
- **Full scope in this same change**: also pre-aggregate the Map view's
  nodes/edges and the call-breakdown drill-down. Deferred - at least one
  more table keyed by `(source, target)` pairs would be needed for edges
  (non-trivial with the `peer.service` effective-service override), and
  likely a third for the breakdown's unbounded per-peer/per-db-operation
  cardinality. Left as a named roadmap follow-up rather than growing this
  change into a substantially larger one.
- **Storing `ResourceAttributes` (or a fixed allow-listed subset of keys)
  per aggregate row**, so the pre-aggregated path could also serve
  filtered requests. Rejected: the filter chips are genuinely arbitrary
  free text, not a bounded/known set of keys, so a "fixed subset" would
  silently stop working the moment someone filters on a key outside it,
  and a full per-row attribute map reintroduces the per-row cardinality
  this feature exists to eliminate. The live-query fallback for the
  filtered case is simple, always correct, and already the exact query
  that ran before this change.

## Consequences

- The pre-aggregated path can over-count by up to roughly one minute of
  data at each edge of a requested window (the floor/ceil rounding
  above) - a bounded, documented approximation, never a silently dropped
  one. Negligible for the 15m/1h/6h/24h presets; most visible (up to
  ~20% of the window) on the 5m preset. Same "document the known,
  accepted tradeoff" precedent as `ServiceDependencyQueryBuilder`'s
  child-only window-filtering remark and `quantile()`'s own approximate
  (t-digest) nature throughout this codebase.
- This is the first materialized view in the codebase. It's schema-only
  and additive (a `CREATE MATERIALIZED VIEW`, same migration-numbering
  convention as every other table), but it is a new operational surface:
  a broken view (e.g. after a future `spans` schema change this migration
  didn't anticipate) fails silently from the ingest side (thanks to
  `materialized_views_ignore_errors`) but stops `service_metrics` from
  updating - worth checking first if Services-tab numbers ever look
  stale despite spans clearly still arriving.
- No backfill of historical `spans` rows into `service_metrics` - same
  precedent as `EventId`'s own migration (`0002_logs_event_id.sql`) and
  ADR-0007's `PatternId`. A freshly-applied migration's `service_metrics`
  starts empty; the Services tab's pre-aggregated path simply has nothing
  to show until new spans flow in (falls back to an empty result, not a
  crash - `ServiceOverviewQueryBuilder`'s live path is unaffected either
  way since it reads `spans` directly).
- `ServiceMetricsOptions.Enabled: false` is the safety valve if the
  materialized view or `AggregatingMergeTree` merging ever needs to be
  ruled out as a cause of incorrect Services-tab numbers - it doesn't
  disable the view itself (that needs a `DROP VIEW`/migration), only
  Flare.Api's read path.

## Related documentation

- `docs-internal/adr/0007-pattern-clustering-at-flush-time.md` - the
  precedent this decision follows (compute once at flush time, not on
  every read), for a different table and a different reason ClickHouse
  couldn't do it automatically there (Drain clustering isn't expressible
  as a SQL aggregate).
- `docs-internal/adr/0029-query-result-caching.md` - a different, complementary
  answer to "the same expensive query keeps re-running": caching the
  *result* of a query rather than pre-computing the *data* a cheaper
  query can read. Both apply here in principle; this ADR only addresses
  the latter for the Services tab specifically.
- `db/clickhouse/README.md` - migration `0022_service_metrics.sql`.
