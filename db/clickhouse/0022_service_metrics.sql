-- Flare service-metrics schema, migration 0022.
--
-- Pre-aggregated per-service RED metrics (request rate, error rate, p50/p95/p99 duration),
-- computed at span-flush time rather than re-scanned from `spans` on every Traces >
-- Services tab load - see ADR-0030 for the full decision record (why a materialized view
-- rather than a second explicit write from `Flare.Ingest`'s `SpanFlushWorker`, and why this
-- migration covers only the Services tab's Table view, not the Map view's dependency graph
-- or the per-node call breakdown). Same "compute once at flush time, not on every read"
-- precedent as `0010_logs_pattern.sql`'s Drain log-pattern clustering (ADR-0007), applied
-- here to spans instead of logs.
--
-- `service_metrics` only ever serves the Services tab's *unfiltered* request - see
-- `Flare.Api`'s `ServiceOverviewQueryService`, which falls back to the existing live
-- `spans` GROUP BY whenever the tab's resource-attribute filter chips are in use, since
-- those are arbitrary free-text key/value pairs this table has no dimension for.
--
-- `RequestCount`/`ErrorCount` are plain sums (SimpleAggregateFunction) - trivially
-- mergeable across flush batches and across shards. `P50State`/`P95State`/`P99State`
-- are NOT plain sums - percentiles aren't summable - so they're stored as ClickHouse
-- `AggregateFunction(quantile(p), UInt64)` state blobs, produced by `quantileState()` in
-- the materialized view below and combined back into a real percentile via
-- `quantileMerge()` at query time (`Flare.Api`'s `ServiceMetricsQueryBuilder`). This is
-- the standard ClickHouse pattern for exactly this problem - re-deriving it in
-- application code would mean reimplementing t-digest quantile merging in C#, which ADR-0030
-- explicitly rejected.
CREATE TABLE IF NOT EXISTS clickhousedb.service_metrics
(
    -- One-minute buckets: toStartOfMinute(spans.StartTime). Fine enough for the Services
    -- tab's tightest window preset (5m) to span several buckets, coarse enough to keep row
    -- count bounded by (distinct services x minutes), not (distinct services x spans).
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),

    -- Same column, same LowCardinality(String) type as spans.ServiceName.
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),

    -- Root spans (ParentSpanId = '') only - same "a service's root spans are its inbound
    -- request entry points" convention ServiceOverviewQueryBuilder's remarks document,
    -- applied here in the materialized view's WHERE clause instead.
    RequestCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),

    P50State AggregateFunction(quantile(0.5), UInt64),
    P95State AggregateFunction(quantile(0.95), UInt64),
    P99State AggregateFunction(quantile(0.99), UInt64)
)
ENGINE = AggregatingMergeTree
-- Same monthly rationale as spans/logs (`schema-partition-low-cardinality`).
PARTITION BY toStartOfMonth(TimeBucket)
-- ServiceName leads, not TimeBucket - every query groups by ServiceName first
-- (ServiceMetricsQueryBuilder's GROUP BY ServiceName), and this keeps one service's
-- buckets adjacent for cheap merging within a wide window.
ORDER BY (ServiceName, TimeBucket)
SETTINGS index_granularity = 8192;

-- Fires once per block actually inserted into `spans` - i.e. as part of the same
-- `InsertBinaryAsync` call `Flare.Ingest`'s `SpanFlushWorker`/`ClickHouseSpanWriter`
-- already make, no separate write path to keep in sync. See ADR-0030 for why this is safe
-- against double-counting across retried/overlapping flush batches (a plain second
-- time-scoped query would not be) and for the `materialized_views_ignore_errors` insert
-- setting `Flare.Ingest` sets to keep a broken view from failing the primary spans write.
CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_metrics_mv
TO clickhousedb.service_metrics
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    count() AS RequestCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State,
    quantileState(0.99)(DurationNano) AS P99State
FROM clickhousedb.spans
WHERE ParentSpanId = ''
GROUP BY TimeBucket, ServiceName;
