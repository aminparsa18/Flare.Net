-- Browser session replay, migration 0090, cluster variant (see db/clickhouse/0090_web_replay_chunks.sql).
CREATE TABLE IF NOT EXISTS clickhousedb.web_replay_chunks_local ON CLUSTER 'flare_cluster'
(
    StartTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    EndTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SessionId String CODEC(ZSTD(1)),
    ChunkId UUID CODEC(ZSTD(1)),
    EventCount UInt32 CODEC(ZSTD(1)),
    Payload String CODEC(ZSTD(3)),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/web_replay_chunks_local', '{replica}')
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (ServiceName, SessionId, StartTime)
SETTINGS index_granularity = 256;

CREATE TABLE IF NOT EXISTS clickhousedb.web_replay_chunks ON CLUSTER 'flare_cluster' AS clickhousedb.web_replay_chunks_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'web_replay_chunks_local', cityHash64(SessionId))
SETTINGS insert_distributed_sync = 1;
