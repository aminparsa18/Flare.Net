# Clustering: multi-node ClickHouse

Flare's default deployment (`docker-compose.yml`) runs a single ClickHouse
node: every table is a plain `MergeTree`, not `ReplicatedMergeTree`, so
there's no ClickHouse redundancy or sharding. **Cluster mode** is the opt-in
alternative — a real 4-node ClickHouse cluster with `ReplicatedMergeTree`/
`Distributed` tables, coordinated by a 3-node ClickHouse Keeper quorum
instead of ZooKeeper. This page explains the model; see
[`../how-to/run-cluster-mode.md`](../how-to/run-cluster-mode.md) to turn it
on, and [`../reference/clustering-config.md`](../reference/clustering-config.md)
for the exact config keys involved.

Cluster mode does not replace `docker-compose.yml` — that file is untouched
and stays the default. It is also not a live migration path: point
`docker-compose.cluster.yml` at a fresh set of volumes, don't try to convert
an existing single-node deployment's data in place.

## Topology

2 shards × 2 replicas = 4 ClickHouse nodes, plus a 3-node Keeper ensemble for
quorum:

```
flare_cluster
├── shard 1: clickhouse-1, clickhouse-2  (replicas of each other)
└── shard 2: clickhouse-3, clickhouse-4  (replicas of each other)

keeper-1, keeper-2, keeper-3  (raft quorum, coordinates replication + ON CLUSTER DDL)
```

This topology proves both halves of "horizontal availability/throughput":
replicas provide redundant copies of each shard's data, so another replica
can serve a shard when a node becomes unavailable — whether that failover is
actually seamless for the application depends on routing/failure behavior,
not on replication alone (see "ClickHouse load balancing" below for the
concrete limits of this deployment's routing). Shards give throughput
headroom, splitting write/query load across two independent groups. A real
deployment can extend this pattern — more shards for more throughput, more
replicas per shard for more availability — following the same
macros/remote-servers config convention.

## How tables are shared across the cluster

Every table becomes two objects: `<name>_local` (the real replicated
storage, one per shard/replica) and `<name>` itself, a `Distributed` table
forwarding to `<name>_local` across the cluster — so every existing
unqualified table reference in the app keeps working unchanged. Rows are
sharded by `rand()` for most tables, except `spans`, which shards on the
trace id so a whole trace's spans stay on one shard. **Why this naming and
sharding shape was chosen, the alternatives considered, and the real
data-correctness hazard around changing the sharding key are recorded in
[ADR-0003](../../docs-internal/adr/0003-distributed-tables-plain-names-and-sharding.md)**
— this page describes the resulting behavior, not the decision itself.

## How schema gets applied

There's no `docker-entrypoint-initdb.d` mount for the cluster's ClickHouse
nodes (unlike the single-node `docker-compose.yml`'s one `clickhouse`
service). Schema application goes entirely through
`ClickHouseMigrationRunner` — `Flare.Ingest`/`Flare.Api` both call it
unconditionally at startup, same as the single-node path, but
`ClickHouse:ClusterMode=true` switches it to the `db/clickhouse-cluster/*.sql`
schema set and to `ON CLUSTER 'flare_cluster'`-wrapped bootstrap statements.
Running each node's own first-boot init hook independently would be
redundant and timing-sensitive against Keeper's own startup — the runner's
existing idempotent, no-distributed-lock-needed design already handles this
correctly without one.

## ClickHouse load balancing: `clickhouse-lb`

`ConnectionStrings__clickhousedb` for `ingest-1`, `ingest-2`, and `api` all
point at `clickhouse-lb:8123` — an `nginx:alpine` service that round-robins
ClickHouse's HTTP interface across all 4 nodes with passive failover
(`max_fails`/`fail_timeout`, and `proxy_next_upstream` retrying the next
node on a connect error or 5xx). Any of the 4 nodes is a valid target
regardless of shard: `Distributed` tables live on every node and forward
each request to the correct shard/replica internally, so round-robining
without shard awareness is correct, not just convenient.

This is a plain reverse-proxy approach, not ClickHouse's purpose-built
`chproxy` — nginx is enough to solve "a single dead node no longer breaks
ingest/query" without pulling in `chproxy`'s own config format and
user-routing model for functionality this deployment doesn't need yet. A
deployment with heavier requirements (per-user query queues/limits, more
elaborate routing) may still prefer `chproxy` — swapping it in only touches
this one service, the `Distributed`-table correctness above doesn't change.

**Explicit limitation**: `clickhouse-lb` provides transport-level
availability, not ClickHouse-aware shard/replica routing.
`proxy_next_upstream` only retries the next node on a connect error or 5xx —
"this node is unreachable." It has no notion of ClickHouse's own response
semantics, so "this node is reachable but the requested operation can't be
completed" (a slow/failed query, a resource-limit rejection, a node that's
up but lagging or mid-recovery) is not a case nginx retries or routes
around — the request just fails or returns whatever that node gave back,
even though a healthier node might have served it fine. `chproxy` (or
ClickHouse's own client-side retry/load-balancing policies) is the place to
look if that distinction starts to matter operationally.

## Drain log-pattern clustering across replicas

`DrainPatternMatcher` (the log-pattern-detection engine behind the Logs
page's Patterns modal) does its masking/tokenizing/similarity work the same
way regardless of cluster mode, but where it stores cluster state is
pluggable behind an `IPatternClusterStore` seam:

- **In-memory** (default) — each `Flare.Ingest` replica keeps its own
  per-process cluster tree. Correct for a single replica; with two replicas
  consuming off the same shared Redis Stream, the same log template can end
  up under a different `PatternId` on each replica, fragmenting the Logs
  page's pattern grouping.
- **Redis-backed** (opt-in) — one Redis key per `(tokenCount, firstToken)`
  bucket, read/written via a compare-and-swap conditional transaction so two
  racing replicas can't clobber each other. A whole flush batch is grouped
  by bucket first, so this is normally one Redis round trip per distinct
  template in the batch, not per log line. Eviction is TTL-based rather
  than the in-memory store's exact LRU cap.

`docker-compose.cluster.yml` enables the Redis-backed store on both ingest
replicas so the two-replica deployment actually exercises it. See
[`../reference/clustering-config.md`](../reference/clustering-config.md) for
the exact config keys.

## Dashboard: cluster status on the Indexing page

`GET /api/indexing/cluster` backs a panel on the **Indexing** page
(`/indexing`, not `/resources` — Resources' Docker-based pollers are
explicitly single-host concepts, unrelated to ClickHouse cluster state).
Renders nothing on a default single-node deployment — the endpoint skips
querying ClickHouse entirely when `ClickHouse:ClusterMode` is off. When
cluster mode is on, it shows:

- **Topology**, grouped by shard, from `system.clusters`.
- **Per-node reachability** — `errors_count` per row, the *connecting*
  node's own view of that peer (not a cluster-wide consensus) — good enough
  for "can I reach this node right now," but not a replication-currency
  signal.
- **Per-node replication status** — `max(absolute_delay)`/`sum(queue_size)`
  per node from `system.replicas`, collapsing however many replicated
  tables live on a node to "the worst any single one of them is behind."
  Renders "In sync" when both are zero, a "Queue N · Ns" warning otherwise,
  or a plain "—" when the `system.replicas` read itself failed — the
  dashboard deliberately never shows a bare `0` in that failure case, since
  that would read as "caught up" when it actually means "unknown."
- **Shared pattern store on/off** — mirrored from `LogPattern:SharedStore`
  purely for display, so the setting above is visible on the dashboard
  instead of only discoverable by reading this doc or the compose file.

**Deliberately out of scope**, named rather than silently skipped: Keeper
quorum health (no notion of it in `system.clusters` — Keeper speaks its own
protocol over a separate connection, not SQL) and `system.replication_queue`'s
own per-entry detail (what specifically is stuck). Keeper quorum health is
the priority follow-up — the panel today can only ever say "ClickHouse
looks healthy and synchronized," with no way to surface a quorum that's
lost a node or failing to elect a leader, even though Keeper degradation
doesn't necessarily show up as replication lag right away. The target shape
once Keeper is added:

```
Cluster
──────────────────────────────
ClickHouse       ● Healthy
Replication      ● In sync
Keeper quorum    ● 3 / 3 healthy

Shards
  Shard 1        ● Healthy
  Shard 2        ● Healthy
```

i.e. Keeper as its own top-level row (queried via its `mntr`/`ruok`
protocol against the Keeper nodes directly, not through ClickHouse SQL),
not folded into or inferred from the ClickHouse node rows above it.

For the concrete bugs found while building and verifying all of the above
against a real running cluster, see
[the operational-notes investigation](../../docs-internal/investigations/clickhouse-cluster-operational-notes.md).