# Investigation: does a ClickHouse materialized view's self-join see the persisted table, or only the new block?

Date: 2026-09-21
Related: `docs-internal/adr/0031-service-map-breakdown-pre-aggregation.md` (deferred
edges pending this spike), `docs-internal/planning/roadmap.md` ("Dependency-graph
edges pre-aggregated at flush time, not queried live").

## Problem statement

ADR-0031 pre-aggregated the Services-tab Map view's nodes and per-node call
breakdown, but left edges (`ServiceDependencyQueryBuilder`'s live self-join over
`spans`) on the live query path. Its Alternatives section sketched a dual-MV
design — two materialized views on `spans`, one triggered per newly-arrived
child (self-joining to find its already-committed parent) and one triggered per
newly-arrived parent (self-joining to find its already-committed children) — but
flagged that **same-batch (single-INSERT) parent+child visibility inside a
materialized view's self-join is unverified ClickHouse behavior**, and declined
to ship it without confirming that behavior first (the same kind of live spike
ADR-0030 ran for `materialized_views_ignore_errors`). This investigation runs
that spike.

## What was checked

A standalone, throwaway ClickHouse 26.8.2.7 container (`clickhouse/clickhouse-server:latest`,
isolated from the real app's `docker-compose.yml` stack/volumes — nothing
persisted, nothing at risk), with a scratch schema mirroring the real
`db/clickhouse/0007_spans.sql` columns relevant to the join
(`TraceId`/`SpanId`/`ParentSpanId`/`ServiceName`/`StartTime`/`DurationNano`) and
the exact two-MV design ADR-0031 sketched:

```sql
CREATE TABLE spike.service_dependency_edges_spike
(
    TimeBucket DateTime, Source LowCardinality(String), Target LowCardinality(String),
    CallCount SimpleAggregateFunction(sum, UInt64), TotalDurationNano SimpleAggregateFunction(sum, UInt64)
)
ENGINE = AggregatingMergeTree
ORDER BY (Source, Target, TimeBucket);

-- MV A: newly-inserted span = candidate CHILD; self-join to find its parent.
CREATE MATERIALIZED VIEW spike.edges_from_child_mv TO spike.service_dependency_edges_spike AS
SELECT toStartOfMinute(child.StartTime) AS TimeBucket, parent.ServiceName AS Source, child.ServiceName AS Target,
       count() AS CallCount, sum(child.DurationNano) AS TotalDurationNano
FROM spike.spans_spike AS child
INNER JOIN spike.spans_spike AS parent ON parent.TraceId = child.TraceId AND parent.SpanId = child.ParentSpanId
WHERE child.ParentSpanId != '' AND parent.ServiceName != child.ServiceName
GROUP BY TimeBucket, Source, Target;

-- MV B: newly-inserted span = candidate PARENT; self-join to find already-committed children.
CREATE MATERIALIZED VIEW spike.edges_from_parent_mv TO spike.service_dependency_edges_spike AS
SELECT toStartOfMinute(child.StartTime) AS TimeBucket, parent.ServiceName AS Source, child.ServiceName AS Target,
       count() AS CallCount, sum(child.DurationNano) AS TotalDurationNano
FROM spike.spans_spike AS parent
INNER JOIN spike.spans_spike AS child ON child.TraceId = parent.TraceId AND child.ParentSpanId = parent.SpanId
WHERE child.ParentSpanId != '' AND parent.ServiceName != child.ServiceName
GROUP BY TimeBucket, Source, Target;
```

Three experiments, each inspecting the raw (pre-merge) rows written to
`service_dependency_edges_spike` and cross-checked against `system.query_views_log`
(`written_rows`/`read_rows` per MV invocation):

1. **Same-batch**: one `INSERT ... VALUES` containing both a parent row
   (`service-A`) and its child row (`service-B`) together.
2. **Cross-batch, parent-then-child**: two separate `INSERT` statements, 1-2s apart.
3. **Cross-batch, child-then-parent**: two separate `INSERT` statements, 1-2s apart
   (reverse order), re-run three times for consistency.

## Findings

1. **Same-batch: double-counted, not merely "unverified."** The single 2-row
   INSERT triggered *both* MVs, each writing its own edge row
   (`query_views_log`: `written_rows=1` for both `edges_from_child_mv` and
   `edges_from_parent_mv`, `read_rows=4` each). After merging,
   `sum(CallCount)=2` and `sum(TotalDurationNano)=4000000` for a single actual
   call that only happened once (`TotalDurationNano` should have been
   `2000000`). Confirmed via direct inspection of the raw pre-merge rows (two
   identical `(service-A, service-B)` rows) and the merged `GROUP BY` result.

2. **Cross-batch: never caught, in either arrival order, across four repeated
   trials.** Every single-row INSERT that should have found an
   already-committed counterpart from an earlier, separate INSERT produced
   `written_rows=0`. `query_views_log.read_rows` for every cross-batch trigger
   was 1-2 — consistent only with the join having scanned the newly-inserted
   block itself, never the accumulated persisted table (which had ≥1 unrelated
   or matching row present in every trial). A plain, non-MV `SELECT` with the
   identical self-join logic, run manually against the same committed table
   state immediately afterward, found the pair correctly (`CallCount=1`) —
   proving the data and join predicate were fine; the discrepancy is
   specifically in how a materialized view's trigger resolves a **self**-join.

3. **Root cause, stated precisely**: a materialized view's trigger query
   resolves *every* occurrence of the source table it's attached to — not just
   the literal `FROM` clause, but also a `JOIN` back to the same table by
   name — to the single newly-inserted block. There is no code path in which
   the joined side reads the actually-persisted, already-committed table
   state. This is a stronger and more decisive result than either outcome
   ADR-0031 anticipated (`same-batch double-counts` OR `same-batch silently
   drops`, both hypothesized as narrow edge cases): here, the *common*
   cross-batch case (parent/child from different processes/exporters, landing
   in different flush batches — the case ADR-0031's Context says is "the
   common case, not an edge case") **never produces an edge at all**, while
   the *rare* same-batch case produces a **wrong (doubled) count** instead of
   a correct one.

## Verdict

**Unsafe, and not narrowly fixable by tuning the design sketched in
ADR-0031.** The dual-MV self-join approach doesn't have a same-batch edge
case to patch around — it fails to serve its primary purpose (cross-batch
edges) at all, while introducing a new correctness bug (double-counting) for
the case it does happen to catch. Making self-joins in materialized views see
the destination table's already-committed state isn't something available to
configure or work around from SQL; it would need spans to carry the
information needed to resolve parent/child without a *self*-referencing join
inside the trigger, which is a fundamentally different design, not a fix to
this one — see `docs-internal/planning/roadmap.md`'s residual note.

Edges remain the live, unindexed self-join query
(`ServiceDependencyQueryBuilder`), unchanged. No production code changed as a
result of this investigation.
