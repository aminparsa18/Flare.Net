# Cluster-mode configuration keys

Exact configuration keys for running Flare against a multi-node ClickHouse
cluster. See [`../how-to/run-cluster-mode.md`](../how-to/run-cluster-mode.md)
for how to turn this on and [`../explanation/clustering.md`](../explanation/clustering.md)
for what each of these actually does.

| Key | Env var (Docker Compose) | Type | Default | Meaning |
|---|---|---|---|---|
| `ClickHouse:ClusterMode` | `ClickHouse__ClusterMode` | `bool` | `false` | Switches `Flare.Ingest`/`Flare.Api` to the `db/clickhouse-cluster/*.sql` schema set, wraps bootstrap DDL in `ON CLUSTER 'flare_cluster'`, makes `IndexingQueryService`'s introspection queries cluster-wide (`cluster()`/`clusterAllReplicas()`), and enables `optimize_skip_unused_shards` on trace-by-id lookups (see ADR-0003). |
| `LogPattern:SharedStore` | `LogPattern__SharedStore` | `bool` | `false` | `false` (default): each `Flare.Ingest` replica keeps its own in-memory Drain pattern-cluster tree (`InMemoryPatternClusterStore`). `true`: pattern clusters are shared across replicas via `RedisPatternClusterStore`, keyed by `(tokenCount, firstToken)` bucket. Set on both `ingest-1`/`ingest-2` in `docker-compose.cluster.yml`. |
| `LogPattern:SharedTemplateTtl` | `LogPattern__SharedTemplateTtl` | duration | `72h` | Only applies when `LogPattern:SharedStore=true`. TTL-based eviction for the Redis-backed pattern store (the in-memory store instead uses an exact `MaxTemplates` LRU cap). |

## Related

- `system.clusters`, `system.replicas` — read live by the Indexing page's
  Cluster panel (`GET /api/indexing/cluster`); see
  [`../explanation/clustering.md`](../explanation/clustering.md#dashboard-cluster-status-on-the-indexing-page).