-- Telemetry export settings, migration 0071 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0071_telemetry_exports.sql; Replicated
-- counterpart of the single-node ReplacingMergeTree, same shape as `metric_attribute_rules` (0041).
CREATE TABLE IF NOT EXISTS clickhousedb.telemetry_exports_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Kind UInt8,
    Name String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,
    ConfigJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/telemetry_exports_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.telemetry_exports ON CLUSTER 'flare_cluster' AS clickhousedb.telemetry_exports_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'telemetry_exports_local', rand())
SETTINGS insert_distributed_sync = 1;
