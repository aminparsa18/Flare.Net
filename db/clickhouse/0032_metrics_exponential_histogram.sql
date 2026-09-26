-- OTLP ExponentialHistogram metric storage, migration 0032.
--
-- A fourth metrics table beside 0008_metrics.sql's metrics_gauge/metrics_sum/
-- metrics_histogram, mirroring Flare.Ingest's ExponentialHistogramPointRecord
-- (Model/MetricPointRecord.cs) 1:1 - keep them in sync. Lifts 0008's "ExponentialHistogram
-- deliberately not represented" scope cut: .NET's OpenTelemetry SDK emits this point type
-- whenever an app opts a Meter instrument into Base2ExponentialBucketHistogramConfiguration,
-- and those data points were previously dropped at ingest. Summary stays unsupported. See
-- docs-internal/adr/0060-exponential-histogram-metrics.md.
--
-- Its own table rather than converting to explicit buckets into metrics_histogram: an
-- exponential histogram carries no bucket bounds, only a Scale (bucket i covers
-- (base^i, base^(i+1)], base = 2^(2^-Scale)) that the SDK lowers on its own as the observed
-- range widens - so two points of the same metric can have different bucket layouts, which
-- breaks metrics_histogram's sumForEach(BucketCounts) element-wise merge. Flare.Api merges
-- rows by downscaling to the lowest Scale in the group instead (ExponentialHistogramEstimator).
--
-- Same column prefix, PARTITION BY, ORDER BY, and bloom-filter indexes as the 0008 tables
-- (see that file for the rationale), IngestedAt included up front rather than via a later
-- ALTER like 0011_ingest_receipt_time.sql had to.

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_exponential_histogram
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
    -- Optional on the wire; 0 when absent, same convention as metrics_histogram.Sum.
    Sum Float64 CODEC(ZSTD(1)),

    -- The spec bounds Scale to [-10, 20]; Int32 matches the wire type (sint32) and ZSTD
    -- flattens the per-series repetition, so a narrower type buys nothing measurable.
    Scale Int32 CODEC(ZSTD(1)),
    ZeroCount UInt64 CODEC(ZSTD(1)),
    -- 0 when absent on the wire, which the spec defines as "exact zero only".
    ZeroThreshold Float64 CODEC(ZSTD(1)),

    -- Offset is the bucket index of the first element of the paired counts array, so
    -- element k counts bucket (Offset + k). Negative-range buckets use the same indexing
    -- on the absolute value.
    PositiveOffset Int32 CODEC(ZSTD(1)),
    PositiveBucketCounts Array(UInt64) CODEC(ZSTD(1)),
    NegativeOffset Int32 CODEC(ZSTD(1)),
    NegativeBucketCounts Array(UInt64) CODEC(ZSTD(1)),

    -- Optional on the wire and, unlike Sum, with no neutral default - NULL when absent.
    -- Stored (unlike metrics_histogram, which omits them) because the .NET SDK does set
    -- them for exponential histograms, and they give Flare.Api a true max plus a clamp for
    -- the outermost-bucket percentile estimate.
    Min Nullable(Float64) CODEC(ZSTD(1)),
    Max Nullable(Float64) CODEC(ZSTD(1)),

    IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1)),

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;
