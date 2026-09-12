-- Dashboards schema, migration 0020 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0020_dashboards.sql and the
-- same `ReplicatedReplacingMergeTree` + Distributed pattern as `alert_rules`/`saved_views`
-- (0003_alert_rules.sql's and 0009_saved_views.sql's cluster variants) - including that
-- pattern's confirmed-live FINAL-over-Distributed correctness.
CREATE TABLE IF NOT EXISTS clickhousedb.dashboards_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    LayoutJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/dashboards_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.dashboards ON CLUSTER 'flare_cluster' AS clickhousedb.dashboards_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'dashboards_local', rand())
SETTINGS insert_distributed_sync = 1;
