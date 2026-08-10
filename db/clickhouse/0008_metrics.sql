-- Flare metric storage schema, migration 0008.
--
-- Three tables (metrics_gauge/metrics_sum/metrics_histogram) - one row per OTLP metric
-- data point, mirroring Flare.Ingest's GaugePointRecord/SumPointRecord/HistogramPointRecord
-- models (Model/MetricPointRecord.cs) 1:1 - keep them in sync. Part of the "OTLP traces &
-- metrics" roadmap item (the metrics half, v6 in Planning.md - traces shipped separately
-- as v4). One table per point type, not one denormalized table, adapted from the OTel
-- Collector's own ClickHouse exporter default schema - same vendored-schema lineage note
-- as 0001_logs.sql/0007_spans.sql.
--
-- v1 scope covers Gauge/Sum/Histogram only. ExponentialHistogram and Summary point types
-- are deliberately not represented here (no metrics_exponential_histogram/metrics_summary
-- table) - same "add it when a concrete need exists" precedent 0007_spans.sql used to
-- omit Span Links. Flare.Ingest's OtlpMetricsMapper recognizes both on the wire and drops
-- their data points rather than erroring (see its remarks).

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_gauge
(
    MetricName LowCardinality(String) CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Unit LowCardinality(String) CODEC(ZSTD(1)),

    -- Resource attribute "service.name", same as logs.ServiceName/spans.ServiceName.
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    ResourceSchemaUrl String CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    ScopeSchemaUrl String CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    ScopeAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    -- The data point's own attributes (the OTel metrics equivalent of logs.LogAttributes/
    -- spans.SpanAttributes) - the key/value set that identifies its timeseries.
    DataPointAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    -- Optional on the wire (OTLP's StartTimeUnixNano); Flare.Ingest's
    -- ClickHouseMetricRowMapper defaults it to Time when absent, keeping this column
    -- non-Nullable - same "coalesce null to a sibling required field" convention as
    -- logs.ObservedTimestamp (see db/clickhouse/README.md's "Empty string / NULL
    -- convention").
    StartTime DateTime64(9) CODEC(Delta, ZSTD(1)),
    Time DateTime64(9) CODEC(Delta, ZSTD(1)),

    Value Float64 CODEC(ZSTD(1)),

    -- Skipping index for arbitrary datapoint-attribute filtering, outside the ORDER BY
    -- prefix below - same bloom_filter precedent as logs/spans. No ResourceAttributes
    -- index here (unlike spans): ServiceName already leads ORDER BY below and covers the
    -- one resource-level filter the Query API roadmap pass actually needs (picking a
    -- metric for a given service) - added reflexively only if a concrete query needs it.
    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = MergeTree
-- Same monthly rationale as logs/spans (`schema-partition-low-cardinality`).
PARTITION BY toStartOfMonth(Time)
-- MetricName leads, not ServiceName/TraceId like logs/spans: a metric's natural access
-- pattern is "this metric, this service, over time" (picking one metric to chart) - the
-- Query API roadmap pass this schema exists to serve, not "list every metric a service
-- emits". ServiceName second narrows within a metric name; Time last for the
-- time-bucketed series scan.
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_sum
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

    -- OTel's AggregationTemporality is a fixed 3-value spec enum that doesn't vary per
    -- source library - Enum8 fits per `schema-types-enum`'s cardinality-stability
    -- guidance, same reasoning as spans.StatusCode.
    AggregationTemporality Enum8(
        'AGGREGATION_TEMPORALITY_UNSPECIFIED' = 0,
        'AGGREGATION_TEMPORALITY_DELTA' = 1,
        'AGGREGATION_TEMPORALITY_CUMULATIVE' = 2
    ),
    -- Fixed 2-value flag, UInt8 fits per `schema-types-minimize-bitwidth`, same rationale
    -- as spans.Kind/logs.SeverityNumber.
    IsMonotonic UInt8,

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.metrics_histogram
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
    -- Optional on the wire; defaults to 0 when absent, same non-Nullable-column
    -- convention as StartTime above (also matches the OTLP spec's own invariant that Sum
    -- must be zero when Count is zero).
    Sum Float64 CODEC(ZSTD(1)),
    -- bucket_counts/explicit_bounds: both empty when the exporter reports count/sum only
    -- with no distribution (see the OTLP spec's HistogramDataPoint comment) - a live
    -- spike against Aspire.ClickHouse.Driver (same discipline 0007_spans.sql's Nested
    -- columns used) confirmed InsertBinaryAsync/ExecuteReaderAsync round-trip
    -- Array(UInt64)/Array(Float64) as plain .NET ulong[]/double[] with no special
    -- handling, same shape as spans.Events' desugared arrays.
    BucketCounts Array(UInt64) CODEC(ZSTD(1)),
    ExplicitBounds Array(Float64) CODEC(ZSTD(1)),

    -- Min/Max (optional on the wire) are deliberately not represented here - no feature
    -- in this roadmap slice's Query API pass consumes them (percentiles are computed
    -- from BucketCounts/ExplicitBounds instead); add via a later ALTER TABLE once one
    -- does, same precedent as spans' omitted Links.

    INDEX idx_dp_attr_key mapKeys(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_dp_attr_value mapValues(DataPointAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;
