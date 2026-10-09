-- Status page visitor subscriptions schema, migration 0076 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0076_status_subscribers.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed` (same shape as status_pages' cluster variant, migration 0072).
CREATE TABLE IF NOT EXISTS clickhousedb.status_subscribers_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    PageId UUID,
    Email String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Verified UInt8 DEFAULT 0,
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/status_subscribers_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.status_subscribers ON CLUSTER 'flare_cluster' AS clickhousedb.status_subscribers_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'status_subscribers_local', rand())
SETTINGS insert_distributed_sync = 1;
