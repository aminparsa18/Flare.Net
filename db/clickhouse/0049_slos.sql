-- SLO schema, migration 0049.
--
-- Service level objectives with error budgets and burn-rate alerting - see
-- `docs-internal/adr/0108-slo-error-budgets.md`.
--
-- `span_sli_minute`: per-minute, per-(service, entry-span name) good/total event counts,
-- computed at span-flush time by the materialized view below - same "compute once at flush
-- time, not on every read" precedent as `0022_service_metrics.sql` (ADR-0030). An SLO's
-- window is days long (99.5% over 28d) and burn-rate alerts re-read it every evaluation
-- tick, so scanning `spans` per read would trip the query row caps; this table is bounded by
-- (service x entry-span name x minutes), not by span count.
--
-- An "event" is an *entry span*: a server/consumer-kind span (Kind 2/5 - the same
-- classification `VersionComparisonQueryBuilder` and the messaging pages use) or a root span
-- (ParentSpanId = '', for background jobs that start a trace themselves).
--
-- Latency SLOs count requests under a threshold, and a "fraction under T" can't be derived
-- from a pre-aggregated quantile state, so the view stores a counter per rung of a fixed
-- threshold ladder (50/100/250/500/1000/2500/5000/10000 ms) and a latency SLO's threshold
-- must be one of those rungs (`Flare.Api.Slos.SloLatencyLadder`). Every column is a plain
-- sum, trivially mergeable across flush batches and shards.
--
-- `slos`: the SLO definitions. Same CRUD-via-tombstone `ReplacingMergeTree(UpdatedAt)` shape
-- as `maintenance_windows` (migration 0031), for the same reason.
--
-- `alert_rules.SloConditionJson`: the `SloBurnRate` alert condition (a fifth
-- `AlertConditionKind`), stored opaque like `ExceptionConditionJson` (migration 0019). No
-- `alert_events` change - `ObservedValue`/`ThresholdValue` (migration 0015) carry the burn rate.
--
-- The view only sees spans inserted after it exists. To backfill an existing instance's
-- history (an SLO window is empty until it fills), run once by hand:
--   INSERT INTO clickhousedb.span_sli_minute
--   SELECT toStartOfMinute(StartTime), ServiceName, Name, count(),
--          countIf(StatusCode = 'STATUS_CODE_ERROR'),
--          countIf(DurationNano <= 50000000), countIf(DurationNano <= 100000000),
--          countIf(DurationNano <= 250000000), countIf(DurationNano <= 500000000),
--          countIf(DurationNano <= 1000000000), countIf(DurationNano <= 2500000000),
--          countIf(DurationNano <= 5000000000), countIf(DurationNano <= 10000000000)
--   FROM clickhousedb.spans WHERE Kind IN (2, 5) OR ParentSpanId = ''
--   GROUP BY toStartOfMinute(StartTime), ServiceName, Name;
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0048, run this by hand via
-- `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.span_sli_minute
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    Name LowCardinality(String) CODEC(ZSTD(1)),

    TotalCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),

    Under50ms SimpleAggregateFunction(sum, UInt64),
    Under100ms SimpleAggregateFunction(sum, UInt64),
    Under250ms SimpleAggregateFunction(sum, UInt64),
    Under500ms SimpleAggregateFunction(sum, UInt64),
    Under1000ms SimpleAggregateFunction(sum, UInt64),
    Under2500ms SimpleAggregateFunction(sum, UInt64),
    Under5000ms SimpleAggregateFunction(sum, UInt64),
    Under10000ms SimpleAggregateFunction(sum, UInt64)
)
ENGINE = AggregatingMergeTree
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (ServiceName, Name, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.span_sli_minute_mv
TO clickhousedb.span_sli_minute
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    Name,
    count() AS TotalCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    countIf(DurationNano <= 50000000) AS Under50ms,
    countIf(DurationNano <= 100000000) AS Under100ms,
    countIf(DurationNano <= 250000000) AS Under250ms,
    countIf(DurationNano <= 500000000) AS Under500ms,
    countIf(DurationNano <= 1000000000) AS Under1000ms,
    countIf(DurationNano <= 2500000000) AS Under2500ms,
    countIf(DurationNano <= 5000000000) AS Under5000ms,
    countIf(DurationNano <= 10000000000) AS Under10000ms
FROM clickhousedb.spans
WHERE Kind IN (2, 5) OR ParentSpanId = ''
GROUP BY TimeBucket, ServiceName, Name;

CREATE TABLE IF NOT EXISTS clickhousedb.slos
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- "Availability" | "Latency" - SloKind's string names verbatim.
    Kind LowCardinality(String),

    ServiceName String CODEC(ZSTD(1)),

    -- Entry-span name (the endpoint); '' = every entry span of the service.
    OperationName String CODEC(ZSTD(1)),

    -- Percent of events that must be good over the window, e.g. 99.5.
    TargetPercent Float64,

    -- Latency SLOs only: a rung of the threshold ladder, in ms. 0 for Availability.
    LatencyThresholdMs UInt32,

    WindowDays UInt16,

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS SloConditionJson String DEFAULT '' CODEC(ZSTD(1));
