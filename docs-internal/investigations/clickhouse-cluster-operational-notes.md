# Investigation: standing up the ClickHouse cluster for real

Dates: 2026-08-22 through 2026-08-30, multiple passes
Related: ADR-0003 (Distributed tables + sharding), `docs/explanation/clustering.md`,
`docs/how-to/run-cluster-mode.md`

## Problem statement

Whether `docker-compose.cluster.yml`'s 4-node ClickHouse + 3-node Keeper
cluster actually works — schema application, replication, cross-node
queries, the dashboard's cluster-status panel — could only be answered by
standing it up for real against fresh volumes, not by reading the compose
file or migration SQL. This records what repeated live testing surfaced,
and how each finding was fixed.

## Environment

`docker-compose.cluster.yml`: 4 ClickHouse nodes (2 shards × 2 replicas) +
3-node ClickHouse Keeper quorum, `db/clickhouse-cluster/*.sql` migrations
applied via `ClickHouseMigrationRunner`.

## Findings

1. **Keeper's default config only listens on loopback.** The official
   `clickhouse/clickhouse-keeper` image has no explicit `<listen_host>`,
   defaulting to `127.0.0.1`/`::1` only — every other container got
   `Connection refused` on port 9181. Fixed: each `keeper-N.xml` explicitly
   sets `<listen_host>0.0.0.0</listen_host>` and `<listen_host>::</listen_host>`
   (with `<listen_try>1</listen_try>` so a missing IPv6 stack doesn't fail
   startup).

2. **The Keeper image doesn't auto-merge `config.d/`.** Unlike
   `clickhouse-server`, `clickhouse-keeper`'s entrypoint loads only
   `/etc/clickhouse-keeper/keeper_config.xml` directly — a `config.d/keeper.xml`
   override was silently ignored (confirmed via the container's own
   `keeper_config-preprocessed.xml`, which listed only the base file as an
   input). Fixed: `db/clickhouse-cluster/config/keeper-{1,2,3}.xml` mount
   directly over `keeper_config.xml`, replacing it wholesale.

3. **`Distributed` table queries need an inter-server secret.** Without one,
   a query forwarded from one node to another fails `AUTHENTICATION_FAILED`
   — forwarded connections default to user `default` with no password, not
   the originally-connecting user's `CLICKHOUSE_PASSWORD`. Fixed:
   `remote-servers.xml` sets a `<secret>` on the cluster definition,
   authenticating node-to-node traffic independently of `CLICKHOUSE_PASSWORD`.

4. **`schema_migrations`' replication path must not include the `{shard}`
   macro.** Every app table intentionally shards via `{shard}` in its
   `ReplicatedMergeTree` path — but doing the same for the small,
   cluster-wide `schema_migrations` tracking table meant each shard tracked
   its own independent "applied migrations" history: shard 2 showed an
   empty table even after a shard-1 insert. Fixed: one fixed literal path
   with no `{shard}` substitution, making all 4 nodes replicas of the same
   single table.

5. **`FINAL` over `Distributed` + `ReplicatedReplacingMergeTree` works
   correctly** — tested directly: a create-then-update pair on `alert_rules`
   collapsed to exactly one row (the latest `UpdatedAt`), read consistently
   from all 4 nodes regardless of which shard the query landed on. This had
   been flagged as an unverified risk in earlier migration comments; it
   isn't one.

6. **A shard-connectivity blip during a synchronous `Distributed` insert can
   duplicate rows.** Observed while finding #3 above was still unresolved:
   an `INSERT INTO logs` with `insert_distributed_sync = 1` returned
   successfully, but the unreachable shard fell back to its async on-disk
   retry queue and delivered the batch a second time once connectivity
   returned — 20 inserted rows became 40. This is `Distributed`'s own
   documented at-least-once retry behavior, not a bug (see ADR-0003's
   Consequences) — named here as a known operational edge case under active
   connectivity loss, not something "fixed."

7. **`IndexingQueryService`'s `system.*` introspection queries needed to
   become cluster-wide.** They previously filtered
   `WHERE database = currentDatabase()` against whichever single node they
   connected to, so the Indexing page reflected one node's local state, not
   the whole cluster's. Fixed: branch on `ClickHouse:ClusterMode` — storage/
   skip-index/growth queries go through `cluster('flare_cluster', ...)`
   (one replica per shard, summed via `GROUP BY` so a shard isn't
   double-counted), disk-usage/query-performance queries (genuinely
   per-node) go through `clusterAllReplicas('flare_cluster', ...)`.
   Confirmed live (2026-08-23) against a real 4-node cluster: `spans_local`
   row/growth counts summed correctly (90 inserted rows read back as 90,
   not 180), disk usage summed to the true ~1.96TB across 4 independent
   ~490.9GB disks, query-performance quantiles merged correctly across all
   4 nodes' query logs.

8. **`CREATE DATABASE IF NOT EXISTS clickhousedb` always failed on a truly
   fresh cluster.** Found 2026-08-23 while building `flare start --cluster`.
   Every service's connection string sets `Database=clickhousedb`, fine for
   every other statement (all explicitly qualify `clickhousedb.<name>`), but
   fatal for this one specifically the first time it runs — ClickHouse's
   HTTP interface rejects every query on a session whose default database
   doesn't exist yet, even the `CREATE DATABASE` that would create it.
   Confirmed directly: `clickhouse-client --database clickhousedb --query
   "CREATE DATABASE IF NOT EXISTS clickhousedb"` against a fresh node fails
   `Code: 81 UNKNOWN_DATABASE`, no `--database` flag succeeds. Single-node
   mode never hit this because `docker-entrypoint-initdb.d`'s own copy of
   this statement runs before the app connects; cluster mode has no such
   side channel. Fixed: `ClickHouseMigrationRunner.ApplyAsync` overrides
   just this one call's `QueryOptions.Database` to ClickHouse's own
   always-present `default` database.

9. **Concurrent replicas could transiently crash-loop once on a truly fresh
   cluster.** `ingest-1`/`ingest-2`/`api` call `ApplyAsync` concurrently at
   startup (safe by design for single-node's synchronous `CREATE TABLE`),
   but cluster mode's `ON CLUSTER` DDL propagates across the 4 nodes
   asynchronously — a replica reading `schema_migrations` and applying, say,
   migration 0006 could hit `UNKNOWN_TABLE` on a table an earlier migration
   just created via `ON CLUSTER`, if the load balancer's round robin landed
   it on a node the DDL hadn't reached yet. Fixed:
   `ClickHouseMigrationRunner.WaitForClusterDdlPropagationAsync` polls
   `clusterAllReplicas('flare_cluster', system.{tables,columns,databases})`
   until the object is visible on every node in `system.clusters`, with
   bounded backoff, before the next statement runs or the migration is
   recorded applied.
   - Verifying this live surfaced a second, independent bug: both
     `src/Flare.Ingest/Dockerfile` and `src/Flare.Api/Dockerfile` only
     `COPY db/clickhouse/ db/clickhouse/`, never `db/clickhouse-cluster/` —
     every Docker-built image had zero cluster migrations to apply, no
     error or warning. Confirmed live via a fresh
     `docker compose -f docker-compose.cluster.yml up --build`: all three
     services reached "Application started" having applied none of the 10
     cluster migrations, `schema_migrations` stayed at 0 rows on all 4
     nodes, `api-1` then hit `UNKNOWN_TABLE` on `alert_rules`. Fixed by
     adding the equivalent `COPY db/clickhouse-cluster/ db/clickhouse-cluster/`
     line to both Dockerfiles.
   - Verified together (2026-08-23) across 3 consecutive fresh
     `docker compose down -v` + `up -d` cycles: 0 restarts on
     `ingest-1`/`ingest-2`/`api` every time, no `UNKNOWN_TABLE`/unhandled
     exceptions, all 4 nodes converging on the identical 17-table schema
     with all 10 migrations recorded (including the `ALTER TABLE ... ADD
     COLUMN` case, confirmed present via `system.columns` on all 4 nodes).

10. **`absolute_delay` is `UInt64` in `system.replicas` on this ClickHouse
    version, not `UInt32` as first written** — threw `InvalidCastException`
    on every row while building the dashboard's replication-lag column,
    degrading `ReplicationInfoAvailable` to `false` cluster-wide (every node
    showing "—", not a crash). Fixed: read as `ulong`, matching
    `queue_size`'s own `sum()`.

11. **The `hostName()`-to-`host_name` join initially matched nothing.**
    `hostName()` returned each container's random default hostname (e.g.
    `128827a5a23e`), not `clickhouse-1`/etc. — a container's hostname
    defaults to a random container ID under Docker Compose unless set
    explicitly; the cluster's `{replica}` macro is a hand-set literal,
    unrelated to the container's actual OS hostname. Fixed: each
    `clickhouse-N` service now sets `hostname: clickhouse-N` explicitly in
    `docker-compose.cluster.yml`.

12. **`estimated_recovery_time` is `UInt32` in `system.clusters`, not
    `UInt64` as first assumed** — threw `InvalidCastException` on every
    row, silently degrading the topology endpoint to an empty node list.
    Confirmed via `DESCRIBE TABLE system.clusters` against a running node.
    Fixed: read as `uint`, matching `errors_count`/`slowdowns_count`.

## Conclusion

After findings #10–#12 were fixed, the full failure/recovery path was
exercised, not just the idle case (2026-08-30): stopped `clickhouse-2`,
inserted several million rows directly into `clickhouse-1`, restarted
`clickhouse-2`, and polled `GET /api/indexing/cluster` through its catch-up
window. Confirmed: `replicationInfoAvailable: false` while the node was
down/starting (never a false "in sync" `0`), real non-zero
`replicationQueueSize`/`replicationLagSeconds` mid-catch-up (e.g. queue 4,
7s lag on `clickhouse-2` moments after rejoining), settling back to `0`/`0`
once caught up, with `logs_local` row counts matching exactly across both
replicas (7,050,000 rows each).

All 12 findings above are fixed and live-verified. The cluster reliably
comes up from fresh volumes, applies all migrations, and the dashboard's
Cluster panel (`docs/explanation/clustering.md`) correctly reflects
topology, reachability, and replication currency.

## Unresolved / follow-ups

- **Keeper quorum health has no surfaced signal at all** — `system.clusters`
  has no notion of Keeper's own health (it speaks its own four-letter-word
  protocol over a separate connection, not SQL). The dashboard's Cluster
  panel today can only ever say "ClickHouse looks healthy and synchronized"
  — it has no way to detect a Keeper quorum that's lost a node or is
  failing to elect a leader, even though Keeper degradation doesn't
  necessarily show up as replication lag right away. Flagged as the
  priority follow-up, ahead of:
- `system.replication_queue`'s own per-entry detail (what specifically is
  stuck, e.g. a retrying merge) — the aggregate queue/lag counters the
  panel already has answer "is this node caught up," not "why isn't it."
- No schema/version marker exists to make an incompatible cluster refuse to
  start under a sharding-key change (see ADR-0003's Consequences).