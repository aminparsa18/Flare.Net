-- Saved views schema, migration 0009 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0009_saved_views.sql and
-- the same `ReplicatedReplacingMergeTree` + Distributed pattern as alert_rules
-- (0003_alert_rules.sql's cluster variant) - including that file's confirmed-live
-- FINAL-over-Distributed correctness (see that file's comment for the test details).
CREATE TABLE IF NOT EXISTS clickhousedb.saved_views_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    PageType LowCardinality(String),
    StateJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/saved_views_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.saved_views ON CLUSTER 'flare_cluster' AS clickhousedb.saved_views_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'saved_views_local', rand())
SETTINGS insert_distributed_sync = 1;
