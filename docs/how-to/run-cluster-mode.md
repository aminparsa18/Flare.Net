# How to run Flare in cluster mode

Turn on the opt-in multi-node ClickHouse cluster instead of the default
single-node deployment — useful once you're outgrowing a single ClickHouse
node's redundancy or throughput. For what this actually does and why it's
built this way, see
[the clustering explanation](../explanation/clustering.md); for the exact
config keys, see [the reference](../reference/clustering-config.md).

## Prerequisites

- Docker, same as the standalone deployment.
- A **fresh** set of volumes. Cluster mode is not a live migration path —
  don't point it at an existing single-node deployment's data.

## Steps

1. From the repo root, start the cluster stack instead of the default
   compose file:

   ```sh
   docker compose -f docker-compose.cluster.yml up -d
   ```

   This brings up 4 ClickHouse nodes (2 shards × 2 replicas), a 3-node
   Keeper quorum, two `Flare.Ingest` replicas, `Flare.Api`, and the
   dashboard. `docker-compose.yml` (the single-node default) is untouched
   and unaffected — the two are independent stacks.

2. Wait for all services to report healthy (`docker compose -f
   docker-compose.cluster.yml ps`). Schema application happens
   automatically at startup via each service's `ClickHouseMigrationRunner`
   — there's no separate migration step to run by hand.

## Verify it worked

```sh
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

# Drain pattern-cluster keys, shared across both replicas - send logs matching the same
# template split across ingest-1/ingest-2, then confirm one bucket key holds the merged
# cluster (not two separate per-replica templates):
docker exec <redis container> redis-cli -a flare --no-auth-warning \
  KEYS "flare:patterns:bucket:*"
docker exec <redis container> redis-cli -a flare --no-auth-warning \
  GET "flare:patterns:bucket:<key from above>"

# Cluster-status panel's own endpoint - expect the same 4 rows as the system.clusters
# query above, plus clusterModeEnabled/sharedPatternStoreEnabled/replicationInfoAvailable
# all true, and each node's replicationQueueSize/replicationLagSeconds at or near 0 on an
# idle cluster:
curl -s http://localhost:8080/api/indexing/cluster | jq

# Replication currency, straight from system.replicas, for comparison against the
# endpoint's own replicationQueueSize/replicationLagSeconds per node:
docker exec <clickhouse-1 container> clickhouse-client --user default --password flare \
  --query "SELECT hostName(), database, table, queue_size, absolute_delay FROM system.replicas"
```

Then open the dashboard: the Logs page's Patterns modal should show a single
row/`PatternId` for a repeated template, not two fragmented rows, and the
Indexing page's **Cluster** panel should show all 4 nodes healthy and in
sync.

## Troubleshooting

Cluster mode has a real history of setup issues that were found and fixed
by standing it up against fresh volumes repeatedly — see
[the operational-notes investigation](../../docs-internal/investigations/clickhouse-cluster-operational-notes.md)
if something doesn't come up cleanly; most of what's there is already
fixed in the current compose files, but the investigation records the
symptoms in case a variant of one resurfaces.

One correctness constraint worth knowing before you change anything here:
if you ever need to change how a table is sharded (see
[ADR-0003](../../docs-internal/adr/0003-distributed-tables-plain-names-and-sharding.md)),
existing cluster volumes must be destroyed and recreated first — this is
the same "fresh volumes only" rule as initial setup, but the failure mode
for skipping it is silently wrong query results, not an error.