-- Flare log storage schema, migration 0001.
--
-- One row per OTLP LogRecord (see Flare.Ingest's `LogEvent` model, which this table
-- mirrors 1:1 - keep them in sync). See README.md in this directory for the full
-- rationale, the field->column mapping table, and known trade-offs.
--
-- Origin: adapted from the OpenTelemetry Collector's own ClickHouse exporter default
-- logs schema (a proven, widely-deployed reference), then reviewed against the
-- ClickHouse `clickhouse-best-practices` agent skill (31 rules covering schema design,
-- query optimization, ingestion). Deviations from the skill's rules are called out
-- inline below with the rule name that was checked.
--
-- Explicitly creates + targets the `clickhousedb` database rather than relying on the
-- container's default database: confirmed by running this migration for real (see
-- "How this gets applied" in README.md) that the official clickhouse/clickhouse-server
-- image's /docker-entrypoint-initdb.d scripts run against `default`, not against
-- whatever logical database Aspire's `AddDatabase("clickhousedb")` provisions - there's
-- no CLICKHOUSE_DB env var wired up to tell the entrypoint otherwise. Being
-- self-contained here means this migration is correct regardless of that ordering.
CREATE DATABASE IF NOT EXISTS clickhousedb;

CREATE TABLE IF NOT EXISTS clickhousedb.logs
(
    -- Event time. DateTime64(9) matches OTLP's nanosecond wire format for forward
    -- compatibility, though Flare.Ingest's current .NET mapping path only carries
    -- 100ns (tick) resolution - see README.md's "Timestamp precision" note.
    Timestamp DateTime64(9) CODEC(Delta, ZSTD(1)),

    -- Time the collector observed the event; defaults to Timestamp at insert time if
    -- the OTLP record didn't set one (LogEvent.ObservedTimestamp is nullable - the
    -- inserting sink, a separate roadmap item, is responsible for that fallback).
    ObservedTimestamp DateTime64(9) CODEC(Delta, ZSTD(1)),

    -- Lower-hex trace/span id, empty string if absent. Kept as plain String rather
    -- than UUID/FixedString for interop with the rest of the OTel ecosystem's
    -- hex-string convention (copy-pasteable, matches what other OTel-native tooling
    -- shows) - a deliberate, reviewed deviation from `schema-types-native-types`.
    -- Never LowCardinality: trace/span ids are effectively unique (`schema-types-lowcardinality`).
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),

    -- Low 8 bits of OTLP LogRecord.flags (W3C trace flags).
    TraceFlags UInt8,

    -- Original severity string as the source logger emitted it (e.g. Serilog's
    -- "Information" vs NLog's "Info") - varies per source library, so LowCardinality
    -- rather than Enum8 (`schema-types-enum`: "values may change frequently -> LowCardinality").
    SeverityText LowCardinality(String) CODEC(ZSTD(1)),

    -- OTel SeverityNumber, 0 (unspecified) to 24. UInt8 fits the full range
    -- (`schema-types-minimize-bitwidth`) and keeps range filters like "at least WARN"
    -- (SeverityNumber >= 13) natural, which a validated Enum8 wouldn't add value over.
    SeverityNumber UInt8,

    -- Resource attribute "service.name". Genuinely low cardinality per Flare instance.
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),

    -- The log message body. Free text, potentially large/unicode - plain String.
    Body String CODEC(ZSTD(1)),

    ResourceSchemaUrl String CODEC(ZSTD(1)),
    -- Resource attribute bag (e.g. service.name, deployment.environment, host.name),
    -- flattened to string at ingest (see Flare.Ingest's OtlpLogMapper). Every OTLP
    -- AnyValue variant - string/bool/int/double/bytes/array/kvlist - is stringified
    -- before it reaches this column; this resolves Planning.md's "Map vs JSON vs
    -- dynamic columns" open question in favor of Map(LowCardinality(String), String),
    -- the same approach used by the OTel Collector's own exporter and SigNoz.
    -- Keys are low-cardinality attribute names; values are not (arbitrary attribute
    -- values, e.g. a user id) - `schema-types-lowcardinality` applies to the key type
    -- only, matching its own "< 10,000 unique -> LowCardinality" guidance.
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    ScopeSchemaUrl String CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    ScopeAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    -- Per-LogRecord attributes (the OTel logs equivalent of span attributes) -
    -- arbitrary structured properties, per Planning.md's dashboard filtering requirement.
    LogAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    -- Presence (not just content) marks a record as a named OTel "event" per the logs
    -- data model; empty string means absent (see Flare.Ingest's EmptyToNull normalization).
    EventName LowCardinality(String) CODEC(ZSTD(1)),

    -- Skipping indices for filters that aren't part of the ORDER BY prefix below
    -- (`query-index-skipping-indices`): point lookups by trace id (event detail /
    -- trace-log correlation), arbitrary attribute key/value filters, and body substring
    -- search. Reviewed as "high overall cardinality, low cardinality within a block" -
    -- exactly the skipping-index sweet spot per the skill.
    INDEX idx_trace_id TraceId TYPE bloom_filter(0.001) GRANULARITY 1,
    INDEX idx_res_attr_key mapKeys(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_value mapValues(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_log_attr_key mapKeys(LogAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_log_attr_value mapValues(LogAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_body Body TYPE tokenbf_v1(32768, 3, 0) GRANULARITY 1
)
ENGINE = MergeTree
-- Monthly, not daily: `schema-partition-low-cardinality`'s own "incorrect" example is
-- literally `PARTITION BY toDate(timestamp)`, flagged for unbounded partition growth
-- over years (3650+ partitions over a decade). Flare is a long-running self-hosted
-- service with no v1 TTL to bound that growth (see below), so monthly keeps partition
-- count in the recommended 100-1,000 range for years of operation, while still serving
-- `schema-partition-lifecycle`'s stated purpose (data lifecycle, not query speed) if/when
-- the "Later" retention roadmap item adds a TTL DROP PARTITION policy.
PARTITION BY toStartOfMonth(Timestamp)
-- No TTL clause: Planning.md's roadmap lists "Retention policies + cold storage to
-- RustFS" as a separate "Later" item. Baking in a hardcoded TTL now would preempt that
-- design - this is a deliberate omission, not an oversight.
--
-- ORDER BY: low-to-high cardinality (`schema-pk-cardinality-order`), leading with the
-- two columns Planning.md's dashboard filters by most (`schema-pk-prioritize-filters`):
-- ServiceName (a dev viewing "their" service's logs) then SeverityNumber (a common
-- refinement), then Timestamp for range scans, then TraceId (also has its own skip
-- index above, so its tail position here is a modest locality bonus, not the primary
-- lookup mechanism for it).
--
-- Known, deliberately UNRESOLVED trade-off: this favors "browse one service over a
-- time range" over "browse everything, no service filter" (an all-services live-tail
-- default view), since rows are grouped by ServiceName first within each month
-- partition. If that all-services view proves too slow once the Query API roadmap item
-- is built against real data, the named follow-up is a Timestamp-first projection
-- (addable non-destructively later) - do not try to solve both now.
ORDER BY (ServiceName, SeverityNumber, Timestamp, TraceId)
SETTINGS index_granularity = 8192;

-- No materialized views for the dashboard's "simple charts" (event volume over time,
-- by level, by service) - plain GROUP BY queries against this table suffice for v1.
-- Whether pre-aggregation is worth the complexity is a decision for the Query API
-- roadmap item, once real query latency is known - not this migration's job.
