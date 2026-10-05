-- Cluster-mode variant of ../clickhouse/0053_alert_acknowledgements.sql - see that file and
-- `docs-internal/adr/0124-alert-acknowledgement-and-snooze.md`.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_acknowledgements_local ON CLUSTER 'flare_cluster'
(
    RuleId UUID,
    AckedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    AckedBy String CODEC(ZSTD(1)),
    Kind LowCardinality(String),
    SnoozedUntil Nullable(DateTime64(3)),
    Note String CODEC(ZSTD(1))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/alert_acknowledgements_local', '{replica}')
ORDER BY (RuleId, AckedAt)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.alert_acknowledgements ON CLUSTER 'flare_cluster' AS clickhousedb.alert_acknowledgements_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'alert_acknowledgements_local', cityHash64(RuleId))
SETTINGS insert_distributed_sync = 1;
