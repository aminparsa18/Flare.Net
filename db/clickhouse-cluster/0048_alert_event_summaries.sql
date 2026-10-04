-- Alerting schema, migration 0048 (cluster variant) - see ../clickhouse/0048_alert_event_summaries.sql
-- and `docs-internal/adr/0104-ai-incident-summary.md`.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_event_summaries_local ON CLUSTER 'flare_cluster'
(
    EventId UUID,
    RuleId UUID,
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    Model LowCardinality(String),
    Summary String CODEC(ZSTD(1)),
    Prompt String CODEC(ZSTD(3))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/alert_event_summaries_local', '{replica}')
PARTITION BY toStartOfMonth(CreatedAt)
ORDER BY (RuleId, CreatedAt)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.alert_event_summaries ON CLUSTER 'flare_cluster' AS clickhousedb.alert_event_summaries_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'alert_event_summaries_local', rand())
SETTINGS insert_distributed_sync = 1;
