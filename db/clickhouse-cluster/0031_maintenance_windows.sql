-- Alerting schema, migration 0031 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0031_maintenance_windows.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed` for the new table, same shape as
-- notification_channels' own cluster variant (migration 0016's cluster file); the
-- alert_events column is added to both the `_local` table and its Distributed counterpart.
CREATE TABLE IF NOT EXISTS clickhousedb.maintenance_windows_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    RuleIds Array(UUID),
    StartsAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    EndsAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    Recurrence LowCardinality(String),
    DaysOfWeek Array(UInt8),
    RepeatUntil Nullable(DateTime64(3)),
    TimeZone LowCardinality(String),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/maintenance_windows_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.maintenance_windows ON CLUSTER 'flare_cluster' AS clickhousedb.maintenance_windows_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'maintenance_windows_local', rand())
SETTINGS insert_distributed_sync = 1;

ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SuppressedByWindow String DEFAULT '';
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SuppressedByWindow String DEFAULT '';
