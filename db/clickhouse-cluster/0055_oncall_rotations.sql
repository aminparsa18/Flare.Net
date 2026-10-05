-- Alerting schema, migration 0055 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0055_oncall_rotations.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed` for the new table, same shape as
-- maintenance_windows' cluster variant (migration 0031); the alert_rules column is added to
-- both the `_local` table and its Distributed counterpart.
CREATE TABLE IF NOT EXISTS clickhousedb.oncall_rotations_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    ChannelIds Array(UUID),
    ShiftHours UInt32,
    StartsAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/oncall_rotations_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.oncall_rotations ON CLUSTER 'flare_cluster' AS clickhousedb.oncall_rotations_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'oncall_rotations_local', rand())
SETTINGS insert_distributed_sync = 1;

ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EscalationRotationId Nullable(UUID);
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EscalationRotationId Nullable(UUID);
