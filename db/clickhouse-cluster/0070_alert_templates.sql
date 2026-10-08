-- Alerting schema, migration 0070 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0070_alert_templates.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed` for the new table (same shape as
-- maintenance_windows' cluster variant, migration 0031); the alert_rules column is added to
-- both the `_local` table and its Distributed counterpart.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_templates_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    IsDefault UInt8 DEFAULT 0,
    TitleTemplate String CODEC(ZSTD(1)),
    BodyTemplate String CODEC(ZSTD(1)),
    ResolvedBodyTemplate String CODEC(ZSTD(1)),
    ChannelBodiesJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/alert_templates_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.alert_templates ON CLUSTER 'flare_cluster' AS clickhousedb.alert_templates_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'alert_templates_local', rand())
SETTINGS insert_distributed_sync = 1;

ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NotificationTemplateId Nullable(UUID);
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NotificationTemplateId Nullable(UUID);
