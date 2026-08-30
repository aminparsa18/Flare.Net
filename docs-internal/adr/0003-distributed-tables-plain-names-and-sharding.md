# ADR-0003: `Distributed` tables keep plain names; `spans` shards on trace id

Status: Accepted
Date: 2026-08-17 (naming/sharding decision) — sharding-key refinement 2026-08-23

## Context

Cluster mode (`docker-compose.cluster.yml`, 2 shards × 2 replicas + a 3-node
Keeper quorum) needed every table replicated and sharded across 4 ClickHouse
nodes, without rewriting the large number of call sites across
`Flare.Ingest`'s writers and `Flare.Api`'s query builders that already
reference tables by their plain single-node names (`TableName = "logs"` in
`ClickHouseLogEventWriter.cs`, `FROM logs` in `LogSearchQueryBuilder.cs`, and
every other table the same way). Separately, each table's rows need a
sharding key — how a row is assigned to shard 1 or shard 2 — and that choice
has different correctness implications depending on whether a table has any
single-entity locality concern (a trace's spans do; log lines don't).

## Decision

1. **Every table becomes two**: `<name>_local` (the real
   `ReplicatedMergeTree`/`ReplicatedReplacingMergeTree` storage, one per
   shard/replica) and `<name>` itself, a `Distributed` table forwarding to
   `<name>_local` across the cluster. The plain name stays the
   application-facing one.
2. `Distributed` tables are created with
   `SETTINGS insert_distributed_sync = 1` — an insert into `logs`
   synchronously fans out to shards instead of buffering async on the local
   node first, trading a little insert latency for not having a
   "row isn't visible on the other shard yet" window.
3. **Sharding key is `rand()`** (even distribution) for every table except
   `spans`, which shards on **`cityHash64(TraceId)`** instead, so every span
   of a given trace lands on the same shard — matching the single-node
   table's own `ORDER BY (TraceId, ...)` physical adjacency, and letting
   `SpanQueryService.GetTraceAsync` set `optimize_skip_unused_shards` (only
   under `ClickHouse:ClusterMode`) so a trace-by-id lookup can prune the
   shard that provably can't hold a given `TraceId`, instead of fanning out
   to both and merging. Best-effort, not forced
   (`force_optimize_skip_unused_shards` stays unset) — if ClickHouse can't
   determine the shard, it falls back to querying every shard, not an error.

## Alternatives considered

- **Distinct names for the `Distributed` tables** (e.g. `logs_distributed`),
  updating every call site to the new name. Rejected: would have required
  auditing and rewriting every table reference in `Flare.Ingest`/`Flare.Api`
  for no behavioral benefit — the plain-name convention is exactly why
  **zero code changes were needed** in either project's writers or query
  builders to support cluster mode at all.
- **`rand()` sharding for `spans` too**, uniform with every other table.
  Rejected: would scatter a single trace's spans across both shards,
  defeating any shard-pruning optimization for trace-by-id lookups and
  forcing every such query to fan out to all shards and merge — the general
  `rand()` default is the right choice for tables with no single-entity
  locality concern (`logs`, `metrics_*`, `alert_rules`, etc.), but `spans`
  specifically has one.

## Consequences

- **Changing `spans`' sharding key is a one-way, non-backward-compatible
  move.** `optimize_skip_unused_shards` assumes every row in `spans_local`
  was actually routed by `cityHash64(TraceId)` — not true for data a cluster
  accumulated under the old `rand()` key before this decision. Pruning a
  shard for a trace with older, `rand()`-routed spans doesn't degrade
  gracefully; it silently returns a complete-looking, wrong answer (the
  pruned shard is never queried, so there's no partial-result signal).
  Existing cluster volumes must be destroyed and recreated before enabling
  trace-by-id pruning against a cluster that ever ran under the old key —
  same "fresh volumes only" posture cluster mode already has for any schema
  change, but worth restating because this failure mode is silent wrong
  data, not a visible error.
- **Known limitation**: there is no schema/version marker that makes an
  incompatible cluster refuse to start under the new pruning behavior — this
  currently relies on an operator having read this ADR (or
  `docs/how-to/run-cluster-mode.md`) rather than the system enforcing it.
- A shard-connectivity blip during a synchronous `Distributed` insert can
  duplicate rows (`Distributed`'s own documented at-least-once retry
  behavior under connectivity failure) — the same at-least-once posture the
  rest of Flare's ingest pipeline already has via Redis Streams (ADR-0002),
  not considered a bug.
- Verified live (2026-08-23) against a real 4-node cluster: 90 spans across
  30 distinct trace IDs showed zero cross-shard overlap, and
  `optimize_skip_unused_shards` was confirmed to actually prune (compared
  `system.query_log` across all 4 nodes with and without the setting, both
  directions — a shard-1 trace and a shard-2 trace).

## Related documentation

- `docs/explanation/clustering.md` — topology and mechanism overview
- `docs/how-to/run-cluster-mode.md` — activating cluster mode
- `docs-internal/investigations/clickhouse-cluster-operational-notes.md` —
  the broader set of issues found standing this cluster up for real
- ADR-0002 — Redis Streams buffering (same at-least-once delivery posture)