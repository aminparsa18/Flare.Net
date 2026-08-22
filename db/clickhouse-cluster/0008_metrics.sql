-- Flare metric storage schema, migration 0008 - CLUSTER VARIANT.
--
-- Same three tables/columns/PARTITION BY/ORDER BY as db/clickhouse/0008_metrics.sql (see
-- that file for the full rationale). Same `_local` (ReplicatedMergeTree) +
-- Distributed-keeps-the-name pattern as logs/spans.
CREATE TABLE IF NOT EXISTS clickhousedb.metrics_gauge_local ON CLUSTER 'flare_cluster'
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
    Value Float64 CODEC(ZSTD(1)),

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/metrics_gauge_local', '{replica}')
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_gauge ON CLUSTER 'flare_cluster' AS clickhousedb.metrics_gauge_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'metrics_gauge_local', rand())
SETTINGS insert_distributed_sync = 1;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_sum_local ON CLUSTER 'flare_cluster'
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
    Value Float64 CODEC(ZSTD(1)),
    AggregationTemporality Enum8(
        'AGGREGATION_TEMPORALITY_UNSPECIFIED' = 0,
        'AGGREGATION_TEMPORALITY_DELTA' = 1,
        'AGGREGATION_TEMPORALITY_CUMULATIVE' = 2
    ),
    IsMonotonic UInt8,

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/metrics_sum_local', '{replica}')
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_sum ON CLUSTER 'flare_cluster' AS clickhousedb.metrics_sum_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'metrics_sum_local', rand())
SETTINGS insert_distributed_sync = 1;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_histogram_local ON CLUSTER 'flare_cluster'
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
    BucketCounts Array(UInt64) CODEC(ZSTD(1)),
    ExplicitBounds Array(Float64) CODEC(ZSTD(1)),

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/metrics_histogram_local', '{replica}')
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_histogram ON CLUSTER 'flare_cluster' AS clickhousedb.metrics_histogram_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'metrics_histogram_local', rand())
SETTINGS insert_distributed_sync = 1;
