# ADR-0031: Pre-aggregate Services-tab Map-view nodes and call-breakdown at flush time, defer edges

Status: Accepted

Date: 2026-09-20

## Context

ADR-0030 pre-aggregated the Traces > Services tab's Table view (RED metrics) via a
materialized view fired at span-flush time (`service_metrics_mv` ->
`service_metrics`, `db/clickhouse/0022_service_metrics.sql`), replacing a live
`GROUP BY` over `spans` on every 10s poll. It deliberately scoped out the Map view's
dependency graph (`ServiceDependencyQueryBuilder` - a self-join keyed by the
`peer.service`-overridden "effective service," producing both nodes *and* edges) and
the per-node call breakdown (`ServiceCallBreakdownQueryBuilder` - grouped by
unbounded-cardinality `peer.service`/`db.system`+`db.operation`), calling both "a
bigger, separate effort" without further detail.

Re-examining that deferred scope: the nodes half of the Map view and the call
breakdown are, on inspection, the **same low-risk shape** as `service_metrics_mv` - a
plain `GROUP BY` over `spans`, just keyed by a different dimension (the
`peer.service`-overridden effective service for nodes; `peer.service` or
`db.system`+`db.operation` for the breakdown, instead of `ServiceName`). Neither
needs a self-join. The **edges** half is fundamentally different and materially
harder, for a reason worth spelling out precisely (ADR-0030 didn't):
`SpanFlushWorker.FlushAsync` (`src/Flare.Ingest/Pipeline/SpanFlushWorker.cs:193-217`)
flushes whatever's accumulated in one batch - `ClickHouseSpanWriter.WriteBatchAsync`
inserts it as a single `InsertBinaryAsync` call, with no per-service or per-trace
scoping and no cross-batch ordering guarantee. A genuine cross-service dependency
edge's parent and child spans come from two different processes with two different
OTLP exporters, so they routinely arrive via *different* flush batches - the
`HAVING Source != Target` clause that defines what a "dependency" edge even is
guarantees this is the common case, not an edge case. A materialized view attached to
`spans` sees only the newly-inserted block on its primary `FROM` reference; correctly
catching a cross-batch parent/child pair needs a self-join against the *full*
persisted table, not just the trigger batch - and same-batch (single-INSERT)
parent+child visibility inside that join is unverified ClickHouse behavior that would
need a live spike to trust, the same kind of spike ADR-0030 itself ran for
`materialized_views_ignore_errors` before shipping. Rather than ship a design with an
unverified correctness assumption, edges are left out of this change.

## Decision

**Three new `AggregatingMergeTree` tables + materialized views, all attached to
`spans`, added by `db/clickhouse/0023_service_dependency_breakdown_metrics.sql`** (+
`db/clickhouse-cluster/0023_..._metrics.sql`, same `_local`/`Replicated<Engine>`/
`Distributed` triplet pattern as `0022`'s cluster variant) - no second write path from
`Flare.Ingest`, same reasoning as ADR-0030 (one insert, no double-counting risk across
retried/overlapping batches). All three fire at the same `InsertBinaryAsync` call site
`service_metrics_mv` already uses; `materialized_views_ignore_errors` (already set by
`ClickHouseSpanWriter`) covers them too, no ingest-side changes needed.

- **`service_dependency_nodes`** (Map view nodes): keyed by `(Service, TimeBucket)`,
  where `Service` is the SQL form of `ServiceDependencyQueryBuilder`'s
  `EffectiveServiceExpr`, applied in the view instead of at query time. No
  `ParentSpanId = ''` filter, unlike `service_metrics` - the live nodes query counts
  every span attributed to a service, not just root spans. `SpanCount`/`ErrorCount`/
  `TotalDurationNano` are plain `SimpleAggregateFunction(sum, UInt64)`.
  `TopOperationsState` is `AggregateFunction(topK(3), String)` - first use of
  `topKState`/`topKMerge` in this codebase, produced by `topKState(3)()` in the view
  and combined via `topKMerge(3)()` at read time
  (`ServiceDependencyMetricsQueryBuilder`), same sketch-approximation semantics the
  live query's `topK(3)(Name)` already has.
- **`service_call_breakdown_external`** / **`service_call_breakdown_database`** (the
  per-node drill-down): keyed by `(ServiceName, PeerService, TimeBucket)` and
  `(ServiceName, DbSystem, DbOperation, TimeBucket)` respectively, filtering on the
  literal `ServiceName` column (not the effective-service expression - same
  "this drill-down answers what the real process behind this node actually calls"
  reasoning `ServiceCallBreakdownQueryBuilder`'s remarks already document).
  `CallCount`/`ErrorCount` are plain sums; `P50State`/`P95State` are
  `AggregateFunction(quantile(p), UInt64)` states, same pattern as `service_metrics`.
  ADR-0030's "materially bigger, unbounded cardinality" characterization of this half
  turns out to be a *cost* concern (more distinct `(service, peer)`/
  `(service, db.system, db.operation)` keys than `service_metrics`'s
  `(service)`-only key), not a correctness one - `AggregatingMergeTree` handles a wider
  key space the same way regardless of its cardinality.
- **Same fallback rule as ADR-0030**: `ServiceDependencyQueryService.GetGraphAsync`'s
  nodes query and `ServiceCallBreakdownQueryService.GetBreakdownAsync`'s external/
  database queries read the pre-aggregated tables only when the request carries no
  resource-attribute filter chip and `ServiceDependencyMetricsOptions.Enabled` is
  true; any filter chip present, or the option disabled, falls back to the existing
  live builders, unchanged. One shared options class/valve for both new read paths
  (not two) - they ship in the same migration, and there's no case for disabling one
  without the other.
- The edges query (`ServiceDependencyQueryBuilder`'s self-join) is **untouched** -
  always the live query, unconditionally, since it has no pre-aggregated
  counterpart in this change.
- Same minute-bucket floor/ceil binding as `ServiceMetricsQueryBuilder`
  (`FloorToMinute`/`CeilToMinute`, copied rather than shared - matching that class's
  own precedent) - same bounded, documented over-inclusion-at-the-edges tradeoff as
  ADR-0030.

## Alternatives considered

- **Also pre-aggregate edges in this change, via a dual-triggered self-join MV** (one
  MV per join direction, each joining the new batch's rows against the full
  persisted `spans` table for the other side - correctly handles the common
  cross-batch case, since whichever side arrives second finds the other already
  committed). Deferred, not rejected outright: the *design* is sound for the
  cross-batch case, but same-batch (single-INSERT) parent+child visibility inside a
  materialized view's join is unverified ClickHouse behavior. Shipping it without a
  live spike to confirm that behavior (the same kind of verification ADR-0030 itself
  did for `materialized_views_ignore_errors`) risks silently under-counting exactly
  the edges this feature exists to show - worse than the current always-correct,
  merely-unindexed live query. Left as a precisely-reasoned follow-up rather than a
  vague "bigger effort" note.
- **A second explicit write from `SpanFlushWorker`** for the nodes/breakdown
  aggregates, matching the shape ADR-0030 already rejected for `service_metrics`.
  Rejected for the same reasons: percentile states aren't hand-rollable in C# without
  reimplementing t-digest merging, and a second time-scoped query risks
  double-counting across retried/overlapping flush batches in a way the
  trigger-on-insert materialized view structurally cannot.
- **Storing `ResourceAttributes` per aggregate row**, so the pre-aggregated paths
  could also serve filtered requests. Rejected for the same reason ADR-0030 rejected
  it for `service_metrics`: the filter chips are arbitrary free text, not a bounded
  set of keys.

## Consequences

- Same bounded, documented minute-edge over-inclusion as ADR-0030 (negligible for
  15m/1h/6h/24h presets, most visible on 5m).
- Two more materialized views on `spans`, on top of `service_metrics_mv` - same new
  operational surface ADR-0030 flagged: a broken view fails silently on the ingest
  side but stops the affected table from updating; check first if Map-view or
  breakdown numbers ever look stale despite spans clearly still arriving.
- No backfill of historical `spans` rows - same precedent as `service_metrics`
  and `EventId`'s own migration. Freshly applied, these tables start empty.
- `ServiceDependencyMetricsOptions.Enabled: false` is the rollback valve if either new
  read path (or the `topK`/`quantile` state-merging machinery generally) needs to be
  ruled out as a cause of incorrect numbers - same shape as
  `ServiceMetricsOptions.Enabled`.
- **Edges remain unaddressed.** The Map view's dependency arrows still run the live,
  unindexed self-join over `spans` on every load - see this ADR's Context for the
  precise reason (cross-service parent/child spans routinely split across flush
  batches) and what a correct fix would need (a dual-MV design, gated on a live spike
  verifying same-batch join visibility). `docs-internal/planning/roadmap.md` carries
  this forward as a narrower, still-open item.

## Related documentation

- `docs-internal/adr/0030-service-red-metrics-pre-aggregation.md` - the precedent
  this decision extends (compute once at flush time, not on every read), and the
  origin of the "materially bigger effort" scoping question this ADR resolves for
  two of its three deferred pieces.
- `docs-internal/adr/0007-pattern-clustering-at-flush-time.md` - the original
  "flush time, not query time" precedent both ADR-0030 and this ADR follow.
- `db/clickhouse/README.md` - migration `0023_service_dependency_breakdown_metrics.sql`.
