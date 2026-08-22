-- Alerting schema, migration 0004 - CLUSTER VARIANT.
--
-- Same columns/append-only rationale as db/clickhouse/0004_alert_events.sql. Plain
-- `ReplicatedMergeTree` (this table has no ReplacingMergeTree/FINAL story, same as the
-- single-node version).
CREATE TABLE IF NOT EXISTS clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
(
    EventId UUID,
    RuleId UUID,
    RuleName String CODEC(ZSTD(1)),
    FiredAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    ObservedCount UInt64,
    ThresholdCount UInt64,
    WindowSeconds UInt32,
    NotificationStatus LowCardinality(String),
    NotificationStatusCode Int32,
    NotificationError String CODEC(ZSTD(1))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/alert_events_local', '{replica}')
PARTITION BY toStartOfMonth(FiredAt)
ORDER BY (RuleId, FiredAt)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.alert_events ON CLUSTER 'flare_cluster' AS clickhousedb.alert_events_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'alert_events_local', rand())
SETTINGS insert_distributed_sync = 1;
