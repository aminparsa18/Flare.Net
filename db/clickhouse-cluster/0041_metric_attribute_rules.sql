-- Metric attribute rules schema, migration 0041 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0041_metric_attribute_rules.sql;
-- Replicated counterpart of the single-node ReplacingMergeTree, same shape as
-- `pipeline_rules` (migration 0024).
CREATE TABLE IF NOT EXISTS clickhousedb.metric_attribute_rules_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,
    MetricName String CODEC(ZSTD(1)),
    Mode UInt8,
    AttributesJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/metric_attribute_rules_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metric_attribute_rules ON CLUSTER 'flare_cluster' AS clickhousedb.metric_attribute_rules_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'metric_attribute_rules_local', rand())
SETTINGS insert_distributed_sync = 1;
