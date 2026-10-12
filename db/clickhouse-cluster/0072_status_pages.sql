-- Status pages schema, migration 0072 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0072_status_pages.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed` (same shape as alert_templates' cluster variant, migration 0070).
CREATE TABLE IF NOT EXISTS clickhousedb.status_pages_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Slug String CODEC(ZSTD(1)),
    Title String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 0,
    ComponentsJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/status_pages_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.status_pages ON CLUSTER 'flare_cluster' AS clickhousedb.status_pages_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'status_pages_local', rand())
SETTINGS insert_distributed_sync = 1;
