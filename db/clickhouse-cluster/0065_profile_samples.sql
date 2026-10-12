-- Continuous-profiling sample storage, migration 0065 - CLUSTER VARIANT.
--
-- Same columns/indices/PARTITION BY/ORDER BY as db/clickhouse/0065_profile_samples.sql (see
-- that file for the full rationale). Same `_local` (ReplicatedMergeTree) +
-- Distributed-keeps-the-name pattern as spans (0007_spans.sql's cluster variant).
CREATE TABLE IF NOT EXISTS clickhousedb.profile_samples_local ON CLUSTER 'flare_cluster'
(
    Timestamp DateTime64(9) CODEC(Delta, ZSTD(1)),
    ProfileId String CODEC(ZSTD(1)),
    DurationNano UInt64 CODEC(ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SampleType LowCardinality(String) CODEC(ZSTD(1)),
    SampleUnit LowCardinality(String) CODEC(ZSTD(1)),
    Stack Array(LowCardinality(String)) CODEC(ZSTD(1)),
    StackHash UInt64 MATERIALIZED cityHash64(Stack) CODEC(ZSTD(1)),
    Value Int64 CODEC(ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    SampleAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1)),

    INDEX idx_trace TraceId TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_key mapKeys(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_value mapValues(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/profile_samples_local', '{replica}')
PARTITION BY toStartOfMonth(Timestamp)
ORDER BY (ServiceName, SampleType, toStartOfMinute(Timestamp), StackHash)
SETTINGS index_granularity = 8192;

-- Sharded by ServiceName so one service's samples stay together for its flame-graph queries.
CREATE TABLE IF NOT EXISTS clickhousedb.profile_samples ON CLUSTER 'flare_cluster' AS clickhousedb.profile_samples_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'profile_samples_local', cityHash64(ServiceName))
SETTINGS insert_distributed_sync = 1;
