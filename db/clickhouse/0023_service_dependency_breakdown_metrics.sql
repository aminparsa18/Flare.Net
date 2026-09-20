-- Flare service-dependency/call-breakdown schema, migration 0023.
--
-- Pre-aggregated per-service Map-view node stats and per-node call-breakdown stats,
-- computed at span-flush time rather than re-scanned from `spans` on every Traces >
-- Services tab Map-view load or node drill-down open - see ADR-0031 for the full
-- decision record. Same "compute once at flush time, not on every read" precedent as
-- `0022_service_metrics.sql` (ADR-0030), applied here to the Map view's nodes and the
-- per-node call-breakdown drill-down instead of the Table view's RED metrics.
--
-- Deliberately does NOT cover the Map view's dependency *edges*
-- (`ServiceDependencyQueryBuilder`'s self-join) - see ADR-0031's Context for why:
-- a genuine cross-service edge's parent and child spans routinely land in different
-- `SpanFlushWorker` flush batches (each service exports independently), so a
-- correct edges aggregate needs same-batch-vs-full-table self-join visibility this
-- migration hasn't verified. Edges remain a live query, unchanged.
--
-- `service_dependency_nodes` only ever serves the Map view's *unfiltered* request,
-- and `service_call_breakdown_*` only the per-node drill-down's unfiltered request -
-- same "no dimension for the Services tab's arbitrary resource-attribute filter
-- chips, fall back to the live query when one is present" rule `service_metrics`
-- already established (see `Flare.Api`'s `ServiceDependencyQueryService`/
-- `ServiceCallBreakdownQueryService`).
CREATE TABLE IF NOT EXISTS clickhousedb.service_dependency_nodes
(
    -- One-minute buckets: toStartOfMinute(spans.StartTime). Same granularity/rationale
    -- as `service_metrics.TimeBucket`.
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),

    -- The peer.service-overridden-or-ServiceName "effective service" -
    -- ServiceDependencyQueryBuilder.EffectiveServiceExpr's SQL form, applied here in
    -- the materialized view below instead of at query time.
    Service LowCardinality(String) CODEC(ZSTD(1)),

    -- Every span attributed to this service, not just root spans (unlike
    -- service_metrics.RequestCount) - matches the live nodes query's "how much work,"
    -- not "how many inbound requests" semantics. See ServiceDependencyNode.SpanCount.
    SpanCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),
    TotalDurationNano SimpleAggregateFunction(sum, UInt64),

    -- topK(3) sketch state, first use of topKState/topKMerge in this codebase - same
    -- approximate-sketch semantics the live query's topK(3)(Name) already has, just
    -- mergeable across buckets via topKMerge(3)() at read time instead of recomputed
    -- fresh per request.
    TopOperationsState AggregateFunction(topK(3), String)
)
ENGINE = AggregatingMergeTree
PARTITION BY toStartOfMonth(TimeBucket)
-- Service leads, not TimeBucket - every query groups by Service first, same
-- rationale as service_metrics's ORDER BY.
ORDER BY (Service, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_dependency_nodes_mv
TO clickhousedb.service_dependency_nodes
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], ServiceName) AS Service,
    count() AS SpanCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    sum(DurationNano) AS TotalDurationNano,
    topKState(3)(Name) AS TopOperationsState
FROM clickhousedb.spans
GROUP BY TimeBucket, Service;

-- Per-node call breakdown, "External calls" tab - every span a service emitted
-- carrying a non-empty peer.service attribute, grouped by that attribute's value.
-- Filters on the literal ServiceName column, not the effective-service expression -
-- see ServiceCallBreakdownQueryBuilder's remarks on why (this answers "what does the
-- real process behind this node call," which only makes sense for spans that process
-- actually emitted itself).
CREATE TABLE IF NOT EXISTS clickhousedb.service_call_breakdown_external
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    PeerService LowCardinality(String) CODEC(ZSTD(1)),

    CallCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),

    P50State AggregateFunction(quantile(0.5), UInt64),
    P95State AggregateFunction(quantile(0.95), UInt64)
)
ENGINE = AggregatingMergeTree
PARTITION BY toStartOfMonth(TimeBucket)
-- ServiceName leads - every query filters by the drilled-down-into service first
-- (ServiceCallBreakdownMetricsQueryBuilder's WHERE ServiceName = {service}).
ORDER BY (ServiceName, PeerService, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_call_breakdown_external_mv
TO clickhousedb.service_call_breakdown_external
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    SpanAttributes['peer.service'] AS PeerService,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans
WHERE SpanAttributes['peer.service'] != ''
GROUP BY TimeBucket, ServiceName, PeerService;

-- Per-node call breakdown, "Database" tab - grouped by (db.system, db.operation).
-- db.operation is optional in the OTel semantic conventions (unlike db.system), so it
-- can legitimately group as an empty string - same convention as the live query.
CREATE TABLE IF NOT EXISTS clickhousedb.service_call_breakdown_database
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    DbSystem LowCardinality(String) CODEC(ZSTD(1)),
    DbOperation LowCardinality(String) CODEC(ZSTD(1)),

    CallCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),

    P50State AggregateFunction(quantile(0.5), UInt64),
    P95State AggregateFunction(quantile(0.95), UInt64)
)
ENGINE = AggregatingMergeTree
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (ServiceName, DbSystem, DbOperation, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_call_breakdown_database_mv
TO clickhousedb.service_call_breakdown_database
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    SpanAttributes['db.system'] AS DbSystem,
    SpanAttributes['db.operation'] AS DbOperation,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans
WHERE SpanAttributes['db.system'] != ''
GROUP BY TimeBucket, ServiceName, DbSystem, DbOperation;
