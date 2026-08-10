-- Flare span storage schema, migration 0007.
--
-- One row per OTLP Span (see Flare.Ingest's `SpanRecord` model, which this table
-- mirrors 1:1 - keep them in sync). Part of the "OTLP traces & metrics" roadmap item
-- (traces half only - see Planning.md's Later section); follows 0001_logs.sql's own
-- documented origin (adapted from the OTel Collector's ClickHouse exporter default
-- schema, this time its traces table rather than its logs table).
CREATE TABLE IF NOT EXISTS clickhousedb.spans
(
    -- Lower-hex trace/span id, same convention as logs.TraceId/SpanId (interop with the
    -- rest of the OTel ecosystem's hex-string convention, not UUID/FixedString).
    -- Unlike logs, these are spec-guaranteed present on every span - no synthetic id
    -- column is needed here the way logs.EventId was: (TraceId, SpanId) is already a
    -- natural, effectively-unique key and pagination tiebreaker.
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),

    -- Empty string = root span (OTLP represents "no parent" as an empty/absent field,
    -- same empty-string-means-absent convention as every other nullable string here -
    -- see logs' own "Empty string / NULL convention").
    ParentSpanId String CODEC(ZSTD(1)),

    -- W3C tracestate, opaque passthrough.
    TraceState String CODEC(ZSTD(1)),

    Name LowCardinality(String) CODEC(ZSTD(1)),

    -- OTel SpanKind (0=unspecified..5=consumer) - fixed, spec-defined range, UInt8
    -- fits it exactly (`schema-types-minimize-bitwidth`, same rationale as logs.SeverityNumber).
    Kind UInt8,

    StartTime DateTime64(9) CODEC(Delta, ZSTD(1)),
    EndTime DateTime64(9) CODEC(Delta, ZSTD(1)),

    -- Stored explicitly rather than computed at query time (EndTime - StartTime) so
    -- "slowest spans" sort/filter doesn't need to recompute it per row, and so it can
    -- carry full wire-nanosecond precision even though StartTime/EndTime themselves are
    -- tick-truncated to 100ns by Flare.Ingest's mapping path (see logs' own "Timestamp
    -- precision" note - the same .NET DateTimeOffset limitation applies here).
    DurationNano UInt64 CODEC(Delta, ZSTD(1)),

    -- OTel's Status.Code is a fixed 3-value spec enum (unlike logs.SeverityText, which
    -- varies per source library) - Enum8 fits per `schema-types-enum`'s "values may
    -- change frequently -> LowCardinality" guidance read the other way: this doesn't.
    StatusCode Enum8('STATUS_CODE_UNSET' = 0, 'STATUS_CODE_OK' = 1, 'STATUS_CODE_ERROR' = 2),
    StatusMessage String CODEC(ZSTD(1)),

    -- Resource attribute "service.name", same as logs.ServiceName.
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),

    ResourceSchemaUrl String CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    ScopeSchemaUrl String CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    ScopeAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    -- Per-span attributes (the OTel traces equivalent of logs.LogAttributes).
    SpanAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    -- OTel Span.Events: timestamped annotations on the span, each with its own
    -- name/attributes. ClickHouse desugars `Nested` into three parallel `Array(...)`
    -- columns (Events.TimeUnixNano/Events.Name/Events.Attributes) - confirmed via a
    -- live spike against Aspire.ClickHouse.Driver before this migration was written
    -- that InsertBinaryAsync/ExecuteReaderAsync round-trip these correctly as plain
    -- .NET DateTime[]/string[]/Dictionary<string,string>[] (see Flare.Ingest's
    -- ClickHouseSpanRowMapper).
    --
    -- Span Links are deliberately NOT represented here, not even as empty columns -
    -- same "add a column when there's a concrete need" precedent as
    -- 0005_alert_rules_telegram.sql/0006_alert_rules_email.sql. No feature in this
    -- roadmap slice consumes them; add via a later ALTER TABLE once one does.
    Events Nested
    (
        TimeUnixNano DateTime64(9),
        Name LowCardinality(String),
        Attributes Map(LowCardinality(String), String)
    ),

    -- Skipping indices for filters outside the ORDER BY prefix below
    -- (`query-index-skipping-indices`, same rationale as logs' own skip indices):
    -- service-scoped span search, and arbitrary structured attribute filtering.
    INDEX idx_service ServiceName TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_span_attr_key mapKeys(SpanAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_span_attr_value mapValues(SpanAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_key mapKeys(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_value mapValues(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = MergeTree
-- Same monthly rationale as logs.PARTITION BY (`schema-partition-low-cardinality`) - no
-- TTL yet, same deferral to the "Retention policies" Later roadmap item.
PARTITION BY toStartOfMonth(StartTime)
--
-- ORDER BY: TraceId leads, not ServiceName+time (logs' choice). Known, deliberately
-- UNRESOLVED trade-off, same spirit as logs' own documented one: this optimizes "get
-- full trace by id" (the waterfall/trace-detail view - the feature that actually
-- defines this roadmap slice) over "list/search spans by service+time" (a secondary,
-- logs-explorer-style nicety for this first pass). Covered adequately for v1 by the
-- ServiceName skip index above (span rows within one TraceId group are naturally
-- low-cardinality in ServiceName - a handful of services per trace - exactly the
-- skip-index sweet spot) plus monthly partition pruning by StartTime. Named follow-up
-- if service+time span search becomes a hot path once real usage exists: a
-- StartTime-first projection, addable non-destructively later - don't solve both now.
ORDER BY (TraceId, StartTime, SpanId)
SETTINGS index_granularity = 8192;
