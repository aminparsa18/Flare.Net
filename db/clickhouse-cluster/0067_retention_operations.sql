-- Retention operations, migration 0067 - CLUSTER VARIANT.
--
-- Same columns/versioning rationale as db/clickhouse/0067_retention_operations.sql;
-- replicated counterpart of the single-node ReplacingMergeTree, same shape as
-- `dashboard_schedules` (migration 0066).
CREATE TABLE IF NOT EXISTS clickhousedb.retention_operations_local ON CLUSTER 'flare_cluster'
(
    TransactionId UUID,
    Signal LowCardinality(String),
    Days UInt32,
    Status LowCardinality(String),
    Error String CODEC(ZSTD(1)),
    RequestedBy String CODEC(ZSTD(1)),
    RequestedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/retention_operations_local', '{replica}', UpdatedAt)
ORDER BY (TransactionId, Signal)
TTL toDateTime(RequestedAt) + INTERVAL 365 DAY
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.retention_operations ON CLUSTER 'flare_cluster' AS clickhousedb.retention_operations_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'retention_operations_local', rand())
SETTINGS insert_distributed_sync = 1;
