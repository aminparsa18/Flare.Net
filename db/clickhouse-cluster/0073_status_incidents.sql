-- Status page incidents schema, migration 0073 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0073_status_incidents.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed` (same shape as status_pages' cluster variant, migration 0072).
CREATE TABLE IF NOT EXISTS clickhousedb.status_incidents_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    PageId UUID,
    Title String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    UpdatesJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/status_incidents_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.status_incidents ON CLUSTER 'flare_cluster' AS clickhousedb.status_incidents_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'status_incidents_local', rand())
SETTINGS insert_distributed_sync = 1;
