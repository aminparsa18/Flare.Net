-- OTLP metric exemplars, migration 0086 (docs-internal/adr/0241-metric-exemplars.md).
--
-- An exemplar is a sample measurement an SDK keeps next to a metric point, with the trace and span that were
-- active when it was recorded. It is what lets a latency chart link a slow bucket to a real trace. The
-- .NET SDK emits them for histograms and counters once an instrument is recorded inside an Activity.
--
-- Its own table rather than a Nested column on the four metrics tables: exemplars are read by trace link,
-- never aggregated into a chart, so they must not widen the hot metric rows, and one OTLP point carries
-- several of them (typically one per histogram bucket). One row per exemplar. Only exemplars that carry a
-- TraceId are stored - without one there is nothing to link to.
--
-- `DataPointAttributes` and `ResourceAttributes` repeat the owning point's, so an exemplar can be matched to
-- the series it belongs to with the same label matchers a query uses. `FilteredAttributes` are the
-- attributes the SDK dropped from the series' own set. `PointType` is the table the point was stored in
-- (gauge / sum / histogram / exponential_histogram), so a query can address `<name>_bucket` vs `<name>_total`.
--
-- Same PARTITION BY, ORDER BY and `_retention_days` convention as the metrics tables (0008, 0069), and it
-- is listed under the Metrics retention signal so a TTL rule covers it.
CREATE TABLE IF NOT EXISTS clickhousedb.metric_exemplars
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
ENGINE = MergeTree
PARTITION BY toStartOfMonth(Time)
ORDER BY (MetricName, ServiceName, Time)
SETTINGS index_granularity = 8192;
