-- OTLP ExponentialHistogram metric storage, migration 0032 - CLUSTER VARIANT.
--
-- Same columns/PARTITION BY/ORDER BY as db/clickhouse/0032_metrics_exponential_histogram.sql
-- (see that file and ADR-0060 for the rationale). Same `_local` (ReplicatedMergeTree) +
-- Distributed-keeps-the-name pattern as 0008_metrics.sql's cluster variant.
CREATE TABLE IF NOT EXISTS clickhousedb.metrics_exponential_histogram_local ON CLUSTER 'flare_cluster'
(
    MetricName LowCardinality(String) CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Unit LowCardinality(String) CODEC(ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    ResourceSchemaUrl String CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    ScopeSchemaUrl String CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    ScopeAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    DataPointAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    StartTime DateTime64(9) CODEC(Delta, ZSTD(1)),
    Time DateTime64(9) CODEC(Delta, ZSTD(1)),
    AggregationTemporality Enum8(
        'AGGREGATION_TEMPORALITY_UNSPECIFIED' = 0,
        'AGGREGATION_TEMPORALITY_DELTA' = 1,
        'AGGREGATION_TEMPORALITY_CUMULATIVE' = 2
    ),
    Count UInt64 CODEC(ZSTD(1)),
    Sum Float64 CODEC(ZSTD(1)),
    Scale Int32 CODEC(ZSTD(1)),
    ZeroCount UInt64 CODEC(ZSTD(1)),
    ZeroThreshold Float64 CODEC(ZSTD(1)),
    PositiveOffset Int32 CODEC(ZSTD(1)),
    PositiveBucketCounts Array(UInt64) CODEC(ZSTD(1)),
    NegativeOffset Int32 CODEC(ZSTD(1)),
    NegativeBucketCounts Array(UInt64) CODEC(ZSTD(1)),
    Min Nullable(Float64) CODEC(ZSTD(1)),
    Max Nullable(Float64) CODEC(ZSTD(1)),
    IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1)),

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/metrics_exponential_histogram_local', '{replica}')
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_exponential_histogram ON CLUSTER 'flare_cluster' AS clickhousedb.metrics_exponential_histogram_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'metrics_exponential_histogram_local', rand())
SETTINGS insert_distributed_sync = 1;
