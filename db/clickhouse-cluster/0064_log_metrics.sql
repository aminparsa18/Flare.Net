-- Log-based metrics schema, migration 0064 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0064_log_metrics.sql;
-- Replicated counterpart of the single-node ReplacingMergeTree, same shape as
-- `metric_attribute_rules` (migration 0041).
CREATE TABLE IF NOT EXISTS clickhousedb.log_metrics_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,
    MetricName String CODEC(ZSTD(1)),
    ConditionJson String CODEC(ZSTD(1)),
    GroupByJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/log_metrics_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.log_metrics ON CLUSTER 'flare_cluster' AS clickhousedb.log_metrics_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'log_metrics_local', rand())
SETTINGS insert_distributed_sync = 1;
