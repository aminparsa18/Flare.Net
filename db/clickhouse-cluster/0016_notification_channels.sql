-- Alerting schema, migration 0016 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0016_notification_channels.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed`, same shape as alert_rules' own cluster
-- variant (migration 0003's cluster file) - see that file's comment for the confirmed-live
-- `FINAL` + `Distributed` reasoning, which applies here unchanged.
CREATE TABLE IF NOT EXISTS clickhousedb.notification_channels_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Type LowCardinality(String),
    WebhookUrl String CODEC(ZSTD(1)),
    TelegramBotToken String CODEC(ZSTD(1)),
    TelegramChatId String CODEC(ZSTD(1)),
    EmailTo String CODEC(ZSTD(1)),
    PagerDutyRoutingKey String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/notification_channels_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.notification_channels ON CLUSTER 'flare_cluster' AS clickhousedb.notification_channels_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'notification_channels_local', rand())
SETTINGS insert_distributed_sync = 1;
