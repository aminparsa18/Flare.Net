-- Error screenshots from client apps, migration 0079, cluster variant (see db/clickhouse/0079_app_screenshots.sql).
CREATE TABLE IF NOT EXISTS clickhousedb.app_screenshots_local ON CLUSTER 'flare_cluster'
(
    StartTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SessionId String CODEC(ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    ContentType LowCardinality(String) CODEC(ZSTD(1)),
    ImageBase64 String CODEC(LZ4),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/app_screenshots_local', '{replica}')
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (ServiceName, SessionId, StartTime)
SETTINGS index_granularity = 256;

CREATE TABLE IF NOT EXISTS clickhousedb.app_screenshots ON CLUSTER 'flare_cluster' AS clickhousedb.app_screenshots_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'app_screenshots_local', cityHash64(SessionId))
SETTINGS insert_distributed_sync = 1;
