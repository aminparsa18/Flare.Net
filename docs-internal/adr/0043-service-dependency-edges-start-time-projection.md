# ADR-0043: StartTime projection for the service-dependency edges query

Status: Accepted

Date: 2026-09-24

## Context

ADR-0031 pre-aggregated the Services-tab Map view's nodes and per-node call
breakdown, but left the **edges** (`(source, target)` pairs) on the live
self-join in `ServiceDependencyQueryBuilder`. The dual-triggered
materialized-view design it sketched for edges was then spiked and found
not viable
([`../investigations/service-dependency-edges-mv-selfjoin-spike.md`](../investigations/service-dependency-edges-mv-selfjoin-spike.md)).
The remaining alternative was computing edges before spans reach
ClickHouse (a servicegraph-style parent/child pairing store inside
`Flare.Ingest`), a large design with its own TTL, orphan-span, and
redelivery questions.

Before committing to that, the live query was measured. Setup: synthetic
5-span chained traces spread over 7 days in a throwaway database with the
same `spans` schema, ClickHouse 26.8.2.7, 8 CPUs / 7.75 GiB, median of 3
runs. The builder's exact SQL was used:

| Spans stored | 15 min window (default) | 60 min | 24 h | Rows read |
|---|---|---|---|---|
| 1M | 86 ms | 77 ms | 122 ms | 2M |
| 10M | 581 ms | 743 ms | 1.2 s | 20M |
| 50M | 2.8 s | 3.4 s | 7.2 s (925 MiB peak) | 100M |

Every window read the whole table twice (once per side of the join). The
cost grows with retention, not with the selected window. Two causes:

1. `spans` is `ORDER BY (TraceId, StartTime, SpanId)` (0007). With
   `TraceId` first, each trace's spans are scattered across the whole table
   in time order, so a `StartTime` predicate can't prune a single granule.
2. The join's `parent` side had no time predicate at all. The builder's
   remarks had accepted this deliberately, and named a `StartTime`-first
   projection as the follow-up for when the query became a hot path.

Neither change helps alone. A parent bound without the projection still
scans everything (cause 1), and the projection without a parent bound
still builds the join's hash table from the whole table (cause 2).

## Decision

Do both, instead of pre-aggregating edges:

- **Migration 0025** adds `spans_by_start_time`, a projection ordered by
  `StartTime` over exactly the columns the edges query reads:
  - `TraceId`
  - `SpanId`
  - `ParentSpanId`
  - `StartTime`
  - `DurationNano`
  - `ServiceName`
  - `SpanAttributes`

  It then runs `MATERIALIZE PROJECTION` to backfill existing parts as a
  background mutation. The cluster variant adds it to `spans_local` only;
  the Distributed `spans` table has no storage of its own.
- **`ServiceDependencyQueryBuilder`** bounds the parent side to
  `[from - ParentStartSlack, to)`, with `ParentStartSlack` set to 1 hour.
  The slack keeps the existing "a parent that started just before the
  window still counts" behavior.
- **`ClickHouseMigrationRunner`** recognizes `ALTER TABLE ... ON CLUSTER
  ... ADD PROJECTION IF NOT EXISTS` as a fourth cluster-DDL shape. It
  waits on `system.projections` for every node, so the migration's
  `MATERIALIZE PROJECTION` can't reach a node before the projection does.

Measured at 50M spans with both in place: the 15-minute window dropped
from 2.8 s to **54 ms** (369k rows read instead of 100M), 60 minutes from
3.4 s to 121 ms, and 24 hours from 7.2 s to 2.8 s. Results were identical
to the old query, compared by hash. The same check on a real
docker-compose stack showed the API's actual `/api/services/dependencies`
query reading through `spans_by_start_time` (per `system.query_log`) and
returning the same edges as the unbounded SQL.

## Consequences

- **Storage.** Sorted by time, the random `TraceId`/`SpanId` strings
  compress worse than in the base table's `TraceId`-first order. In the
  benchmark the projection took 2.05 GiB against the base table's
  1.01 GiB. This is the main cost. It's accepted because the alternative
  was a new ingest-side subsystem.
- **Missed edges for very old parents.** A call whose parent span started
  more than 1 hour before the window's start no longer draws an edge. That
  matters only for calls that stay open longer than an hour, which aren't
  the request/response calls this view is built for.
- **Resource-attribute chips don't benefit.** `ResourceAttributes` is left
  out of the projection to limit its size. An edges query filtered by a
  resource-attribute chip can't use the projection and falls back to the
  pre-0025 full scan, which is still correct.
- **Scaling.** Edges cost now tracks the window rather than retention. The
  24-hour window is still ~2.8 s at 50M spans because it covers far more
  rows. If that becomes a problem, the ingest-side pairing design is still
  available and hasn't been ruled out.
- **Side effect.** Other live `spans` queries that filter by `StartTime` and
  read only projected columns may now pick up the projection too. That
  wasn't measured.
- **Deletes.** ClickHouse can refuse lightweight `DELETE` on tables with
  projections unless `lightweight_mutation_projection_mode` is set. Nothing
  deletes from `spans` today. Revisit this if the "Retention policies"
  roadmap item adds lightweight deletes rather than a TTL.

## Related documentation

- [ADR-0031](0031-service-map-breakdown-pre-aggregation.md) - pre-aggregated
  Map-view nodes and call breakdown, edges deferred
- [`../investigations/service-dependency-edges-mv-selfjoin-spike.md`](../investigations/service-dependency-edges-mv-selfjoin-spike.md)
- [`../../db/clickhouse/0025_spans_start_time_projection.sql`](../../db/clickhouse/0025_spans_start_time_projection.sql)
