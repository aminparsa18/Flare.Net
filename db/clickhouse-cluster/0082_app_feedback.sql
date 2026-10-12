-- User feedback from client apps, migration 0082, cluster variant (see db/clickhouse/0082_app_feedback.sql).
CREATE TABLE IF NOT EXISTS clickhousedb.app_feedback_local ON CLUSTER 'flare_cluster'
(
    StartTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    FeedbackId UUID,
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SessionId String CODEC(ZSTD(1)),
    UserId String CODEC(ZSTD(1)),
    Name String CODEC(ZSTD(1)),
    Email String CODEC(ZSTD(1)),
    AppVersion LowCardinality(String) CODEC(ZSTD(1)),
    Message String CODEC(ZSTD(3)),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/app_feedback_local', '{replica}')
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (ServiceName, StartTime, FeedbackId)
SETTINGS index_granularity = 256;

CREATE TABLE IF NOT EXISTS clickhousedb.app_feedback ON CLUSTER 'flare_cluster' AS clickhousedb.app_feedback_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'app_feedback_local', cityHash64(FeedbackId))
SETTINGS insert_distributed_sync = 1;
