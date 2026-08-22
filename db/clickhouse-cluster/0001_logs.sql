-- Flare log storage schema, migration 0001 - CLUSTER VARIANT.
--
-- Same table/columns/indices/PARTITION BY/ORDER BY as db/clickhouse/0001_logs.sql (see
-- that file for the full column-by-column rationale - not repeated here) with two
-- structural differences for multi-node ClickHouse (Planning.md's "Multi-node scaling"
-- item, docs/clustering.md):
--   1. The real storage table is `logs_local`, one `ReplicatedMergeTree` per shard/
--      replica (path/replica-name driven by the `{shard}`/`{replica}` macros each node's
--      own macros-N.xml sets - see docs/clustering.md).
--   2. `logs` itself is a `Distributed` table forwarding to `logs_local` across
--      `flare_cluster` - every existing unqualified `"logs"` reference in Flare.Ingest's
--      writers and Flare.Api's query builders keeps working unchanged, since the
--      Distributed table has the exact same name the plain MergeTree table used to have.
--
-- Every CREATE here runs `ON CLUSTER 'flare_cluster'` - ClickHouse's cluster-wide DDL
-- queue (backed by the Keeper ensemble this cluster mode also stands up) fans the
-- statement out to every node itself; this migration only needs to run once, against any
-- one reachable node (see ClickHouseMigrationRunner's clusterMode handling).
CREATE DATABASE IF NOT EXISTS clickhousedb ON CLUSTER 'flare_cluster';

CREATE TABLE IF NOT EXISTS clickhousedb.logs_local ON CLUSTER 'flare_cluster'
(
    Timestamp DateTime64(9) CODEC(Delta, ZSTD(1)),
    ObservedTimestamp DateTime64(9) CODEC(Delta, ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    TraceFlags UInt8,
    SeverityText LowCardinality(String) CODEC(ZSTD(1)),
    SeverityNumber UInt8,
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    Body String CODEC(ZSTD(1)),
    ResourceSchemaUrl String CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    ScopeSchemaUrl String CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    ScopeAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    LogAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    EventName LowCardinality(String) CODEC(ZSTD(1)),

    INDEX idx_trace_id TraceId TYPE bloom_filter(0.001) GRANULARITY 1,
    INDEX idx_res_attr_key mapKeys(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_value mapValues(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_log_attr_key mapKeys(LogAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_log_attr_value mapValues(LogAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_body Body TYPE tokenbf_v1(32768, 3, 0) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/logs_local', '{replica}')
PARTITION BY toStartOfMonth(Timestamp)
ORDER BY (ServiceName, SeverityNumber, Timestamp, TraceId)
SETTINGS index_granularity = 8192;

-- insert_distributed_sync = 1: inserts into `logs` synchronously fan out to shards
-- instead of buffering async on the local node first - a deliberate v1 trade (a little
-- extra insert latency for no "row isn't visible on other shards yet" window), same
-- "documented default, not the only valid choice" spirit as 0001_logs.sql's own ORDER BY
-- trade-off note. rand(): even distribution across shard 1/shard 2 - a
-- ServiceName/TraceId-aware sharding key is a named, non-blocking follow-up.
CREATE TABLE IF NOT EXISTS clickhousedb.logs ON CLUSTER 'flare_cluster' AS clickhousedb.logs_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'logs_local', rand())
SETTINGS insert_distributed_sync = 1;
