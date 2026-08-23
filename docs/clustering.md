# Multi-node ClickHouse (opt-in cluster mode)

Planning.md's "Multi-node scaling" roadmap item named two structural single-node
assumptions and one open question about `Flare.Api`'s statelessness. This document
covers the first: `db/clickhouse/0001_logs.sql` (and every other table) uses plain
`MergeTree`, not `ReplicatedMergeTree`, so today's stack has no ClickHouse redundancy
or sharding. `docker-compose.cluster.yml` (paired with `db/clickhouse-cluster/*.sql`)
is the opt-in alternative - a real 4-node ClickHouse cluster with `ReplicatedMergeTree`/
`Distributed` tables, coordinated by a 3-node ClickHouse Keeper quorum instead of
ZooKeeper.

**This does not replace `docker-compose.yml`.** That file is untouched and stays the
default - single-node, no Keeper, no cluster config, zero disruption to anyone already
running it. Reach for `docker-compose.cluster.yml` only when you actually want
ClickHouse redundancy/sharding.

```
docker compose -f docker-compose.cluster.yml up
```

Not a live migration path between the two - point this at a fresh set of volumes,
don't try to convert an existing single-node deployment's data in place.

## Topology

2 shards × 2 replicas = 4 ClickHouse nodes, plus a 3-node Keeper ensemble for quorum:

```
flare_cluster
├── shard 1: clickhouse-1, clickhouse-2  (replicas of each other)
└── shard 2: clickhouse-3, clickhouse-4  (replicas of each other)

keeper-1, keeper-2, keeper-3  (raft quorum, coordinates replication + ON CLUSTER DDL)
```

This topology proves both halves of "horizontal availability/throughput" the roadmap
item's own wording called out: replicas give availability (either node in a shard can
serve the same data), shards give throughput headroom (write/query load splits across
two independent groups). A real deployment can extend this pattern - add more shards
for more throughput, more replicas per shard for more availability - by following the
same macros/remote-servers config convention below.

## Design decision: `Distributed` tables keep the plain table names

Every table becomes two: `<name>_local` (the real `ReplicatedMergeTree`/
`ReplicatedReplacingMergeTree` storage, one per shard/replica) and `<name>` itself, a
`Distributed` table forwarding to `<name>_local` across the cluster. Every existing
unqualified reference in the app - `TableName = "logs"` in
`ClickHouseLogEventWriter.cs`, `"FROM logs\n"` in `LogSearchQueryBuilder.cs`, and every
other table the same way - keeps working completely unchanged. **Zero code changes
were needed in `Flare.Ingest`'s writers or `Flare.Api`'s query builders** to support
cluster mode; this naming choice is why.

`Distributed` tables are created with `SETTINGS insert_distributed_sync = 1`, so an
insert into `logs` synchronously fans out to shards instead of buffering async on the
local node first - a deliberate v1 trade of a little insert latency for not having a
"the row isn't visible on the other shard yet" window. See "Operational notes" below
for what happens to this guarantee under a shard connectivity blip.

Sharding key is `rand()` for every table except `spans` - even distribution, the simple
v1 choice, and still the right one for tables with no single-entity locality concern
(`logs`, `metrics_*`, `alert_rules`, etc.).

`spans` shards on `cityHash64(TraceId)` instead, so every span of a given trace lands on
the same shard - matching the single-node table's own `ORDER BY (TraceId, ...)` physical
adjacency, rather than scattering one trace's spans across shards the way `rand()` would.
This was the named, non-blocking follow-up from an earlier draft of this doc; fixed by
changing `0007_spans.sql`'s cluster variant directly (not a new migration - cluster mode
has no live-migration path per "Not a live migration path" above, so an already-shipped
migration file is still safe to adjust here; anyone who already stood up a cluster under
the old `rand()` key would need to manually `DROP TABLE clickhousedb.spans ON CLUSTER
'flare_cluster'` and let the next startup's `CREATE TABLE IF NOT EXISTS` recreate it -
`Distributed` tables hold no data of their own, so this loses nothing).

Schema-level co-location alone doesn't make "get full trace by id" queries skip the
shard that provably can't hold a given `TraceId` - `SpanQueryService.GetTraceAsync` now
also sets `optimize_skip_unused_shards` (only under `ClickHouse:ClusterMode`, via
`TraceByIdQueryOptions` - see its own remarks for the full reasoning) so ClickHouse can
prune the other shard instead of fanning out to both and merging. Deliberately
best-effort, not forced (`force_optimize_skip_unused_shards` stays unset) - if
ClickHouse can't determine the shard, this just falls back to querying every shard like
before, not an error.

**This assumes every row in `spans_local` was actually routed by `cityHash64(TraceId)`** -
true for anything inserted since that sharding key was set, but NOT true for any data a
cluster already accumulated under the old `rand()` key before this change; skipping a
shard for a trace with older, `rand()`-routed spans would silently omit them rather than
error. Same "fresh volumes only, no live migration path" posture cluster mode already
has (see "Not a live migration path" above) - not a new risk, just one now live in a
query's behavior instead of only a schema definition.

Confirmed live (2026-08-23) against a real 4-node cluster: inserted 90 spans across 30
distinct trace IDs through the `spans` Distributed table and found zero cross-shard
overlap - every trace's spans landed entirely on one shard. Also confirmed
`optimize_skip_unused_shards` itself actually prunes: ran `TraceByIdQueryBuilder`'s exact
query with and without the setting and checked `system.query_log` across all 4 nodes -
without it, a trace-by-id lookup forwards a sub-query to the shard that holds none of
that trace's data; with it, that shard shows no query_log entry at all, and the query
still returns the correct rows. Verified in both directions (a shard-1 trace and a
shard-2 trace).

## How schema gets applied

There's no `docker-entrypoint-initdb.d` mount for the cluster's ClickHouse nodes
(unlike the base `docker-compose.yml`'s single `clickhouse` service). Schema
application goes entirely through `ClickHouseMigrationRunner` - `Flare.Ingest`/
`Flare.Api` both call it unconditionally at startup, same as the single-node path, but
with `ClickHouse:ClusterMode=true` (set via `ClickHouse__ClusterMode` in
`docker-compose.cluster.yml`) switching it to the `db/clickhouse-cluster/*.sql` schema
set and to `ON CLUSTER 'flare_cluster'`-wrapped bootstrap statements. Confirmed live
that running each node's own first-boot init hook independently would be redundant and
timing-sensitive against Keeper's own startup - the runner's existing idempotent,
no-distributed-lock-needed design (see `ClickHouseMigrationRunner`'s own remarks)
already handles this correctly without that hook.

## Operational notes (confirmed against a real running cluster, 2026-08-22)

Every item below was found and fixed by actually standing up the 4-node cluster + 3-node
Keeper ensemble and applying every migration against it - not assumed from
documentation:

- **ClickHouse Keeper's default config only listens on loopback.** The official
  `clickhouse/clickhouse-keeper` image's base config has no explicit `<listen_host>`,
  which defaults to `127.0.0.1`/`::1` only - every other container got
  `Connection refused` against port 9181 until each `keeper-N.xml` explicitly set
  `<listen_host>0.0.0.0</listen_host>` and `<listen_host>::</listen_host>`
  (`<listen_try>1</listen_try>` alongside them so a missing IPv6 stack doesn't fail
  startup).
- **The keeper image doesn't auto-merge `config.d/`.** Unlike `clickhouse-server`
  (which includes `config.d/*.xml` by default), `clickhouse-keeper`'s entrypoint loads
  only `/etc/clickhouse-keeper/keeper_config.xml` directly - a `config.d/keeper.xml`
  override was silently ignored (confirmed via the container's own
  `keeper_config-preprocessed.xml`, which listed only the base file as an input).
  `db/clickhouse-cluster/config/keeper-{1,2,3}.xml` are mounted directly over
  `keeper_config.xml`, replacing it wholesale, not layered on top.
- **`Distributed` table queries need an inter-server secret.** Without one, a
  `Distributed` table query forwarded from one node to another fails
  `AUTHENTICATION_FAILED` - by default, forwarded connections use user `default` with
  no password, not the originally-connecting user's actual `CLICKHOUSE_PASSWORD`.
  `remote-servers.xml` sets a `<secret>` on the cluster definition, which authenticates
  node-to-node traffic on its own terms, decoupled from `CLICKHOUSE_PASSWORD` entirely.
- **`schema_migrations`' replication path must not include the `{shard}` macro.** Every
  *app* table intentionally shards via `{shard}` in its `ReplicatedMergeTree` path
  (`/clickhouse/tables/{shard}/clickhousedb/<name>_local`) - but doing the same for the
  small, cluster-wide `schema_migrations` tracking table means each shard tracks its
  own independent "applied migrations" history, so a node on shard 2 would show zero
  rows applied even after every migration succeeded (confirmed live: shard 2 saw an
  empty table after a shard-1 insert). Fixed by using one fixed literal path with no
  `{shard}` substitution, making all 4 nodes replicas of the *same* single table.
- **`FINAL` over `Distributed` + `ReplicatedReplacingMergeTree` works correctly** -
  tested directly: a create-then-update pair on `alert_rules` collapsed to exactly one
  row (the latest `UpdatedAt`), read consistently from every one of the 4 nodes
  regardless of which shard the query landed on. This had been flagged as an
  unverified risk in earlier draft migration comments; it isn't one.
- **A shard-connectivity blip during a synchronous `Distributed` insert can duplicate
  rows.** Observed directly while the inter-server secret issue above was still
  unresolved: an `INSERT INTO logs` with `insert_distributed_sync = 1` returned
  successfully, but the shard it couldn't reach fell back to its normal async on-disk
  retry queue, and delivered the batch a second time once connectivity came back -
  20 inserted rows became 40 (each row duplicated exactly once). This is
  `Distributed`'s own documented at-least-once retry behavior under connectivity
  failure, not a bug in these migrations - the same at-least-once spirit as the rest of
  Flare's ingest pipeline (Redis Streams' own `XREADGROUP`/`XACK` retry model). Under
  normal steady-state operation (a cluster that isn't actively losing shard
  connectivity mid-insert) this doesn't arise; it's named here as a known operational
  edge case, not "fixed."
- **`IndexingQueryService`'s `system.*` introspection queries are now cluster-wide under
  `ClickHouse:ClusterMode`.** Previously they filtered `WHERE database = currentDatabase()`
  against whichever single node they connected to, so the Resources page's index/part
  diagnostics reflected one node's local state, not the whole cluster's. Fixed by
  branching each query on the same `ClickHouse:ClusterMode` flag `ClickHouseMigrationRunner`
  reads: the storage/skip-index/growth queries (backed by replicated `_local` tables) now
  go through the `cluster('flare_cluster', ...)` table function - one replica per shard,
  summed via `GROUP BY`, so a shard's data isn't double-counted across its two replicas -
  while the disk-usage and query-performance queries (genuinely per-node, not replicated)
  go through `clusterAllReplicas('flare_cluster', ...)` to cover all 4 nodes. Confirmed
  live (2026-08-23): ran all five query strings verbatim against a real 4-node cluster -
  `spans_local`'s row/growth counts summed correctly across both shards without doubling
  (90 inserted rows read back as 90, not 180), disk usage summed to the true ~1.96TB
  across 4 independent ~490.9GB disks without doubling, and query-performance quantiles
  merged correctly across all 4 nodes' query logs.
- **`spans` now shards on `cityHash64(TraceId)` instead of `rand()`, and
  `SpanQueryService.GetTraceAsync` now sets `optimize_skip_unused_shards` under cluster
  mode.** Keeps one trace's spans on one shard and lets a trace-by-id lookup skip the
  shard that provably can't hold it, instead of fanning out to both and merging - see
  "Design decision" above for the full rationale, including the one real caveat (data
  inserted under the old `rand()` key, if any, isn't safely prunable) and the live
  verification confirming both the co-location and the actual shard pruning.

## Known limitations (not attempted here)

- **No client-side load balancing across cluster entry points.**
  `ConnectionStrings__clickhousedb` in `docker-compose.cluster.yml` points every
  `Flare.Ingest`/`Flare.Api` instance at `clickhouse-1` specifically. If `clickhouse-1`
  is down, the app can't fail over to another node on its own - a real deployment would
  put a load balancer (or ClickHouse's own `chproxy`) in front instead. Named as a
  follow-up, not solved here.

## Verifying it yourself

```sh
docker compose -f docker-compose.cluster.yml up -d

# Cluster topology - expect 4 rows (2 shards x 2 replicas):
docker exec <clickhouse-1 container> clickhouse-client --user default --password flare \
  --query "SELECT cluster, shard_num, replica_num, host_name FROM system.clusters WHERE cluster = 'flare_cluster'"

# Table engines - every app table appears twice (Distributed + Replicated*_local):
docker exec <clickhouse-1 container> clickhouse-client --user default --password flare \
  --query "SELECT name, engine FROM system.tables WHERE database = 'clickhousedb' ORDER BY name"

# Two Flare.Ingest replicas sharing one Redis Streams consumer group - expect two
# distinct, machine/process-derived consumer names, both showing recent activity:
docker exec <redis container> redis-cli -a flare --no-auth-warning \
  XINFO CONSUMERS flare:logs flare-ingest
```
