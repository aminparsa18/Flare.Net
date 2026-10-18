-- Metric exemplars, migration 0086, cluster variant (see db/clickhouse/0086_metric_exemplars.sql).
CREATE TABLE IF NOT EXISTS clickhousedb.metric_exemplars_local ON CLUSTER 'flare_cluster'
(
    MetricName LowCardinality(String) CODEC(ZSTD(1)),
    PointType LowCardinality(String) CODEC(ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    DataPointAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    Time DateTime64(9) CODEC(Delta, ZSTD(1)),
    Value Float64 CODEC(ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    FilteredAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    _retention_days UInt16 DEFAULT 20000,

    INDEX idx_trace_id TraceId TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/metric_exemplars_local', '{replica}')
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metric_exemplars ON CLUSTER 'flare_cluster' AS clickhousedb.metric_exemplars_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'metric_exemplars_local', rand())
SETTINGS insert_distributed_sync = 1;
