-- Service-dependency/call-breakdown schema, migration 0023 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0023_service_dependency_breakdown_metrics.sql
-- (see ADR-0031), same `_local` + `Replicated<Engine>` + `Distributed` wrapper pattern as
-- `service_metrics`/`service_metrics_local` (0022_service_metrics.sql's own cluster
-- variant). Each materialized view attaches to `spans_local` and writes to the matching
-- `_local` table - `spans` shards by `cityHash64(TraceId)`, not by service, so the same
-- service/peer/db grouping key legitimately ends up with partial aggregate states on more
-- than one shard, reconciled via the `Distributed` table + `topKMerge()`/`quantileMerge()`
-- at query time, same as 0022's cluster variant.
CREATE TABLE IF NOT EXISTS clickhousedb.service_dependency_nodes_local ON CLUSTER 'flare_cluster'
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    Service LowCardinality(String) CODEC(ZSTD(1)),
    SpanCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),
    TotalDurationNano SimpleAggregateFunction(sum, UInt64),
    TopOperationsState AggregateFunction(topK(3), String)
)
ENGINE = ReplicatedAggregatingMergeTree('/clickhouse/tables/{shard}/clickhousedb/service_dependency_nodes_local', '{replica}')
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (Service, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.service_dependency_nodes ON CLUSTER 'flare_cluster' AS clickhousedb.service_dependency_nodes_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'service_dependency_nodes_local', cityHash64(Service))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_dependency_nodes_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.service_dependency_nodes_local
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], ServiceName) AS Service,
    count() AS SpanCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    sum(DurationNano) AS TotalDurationNano,
    topKState(3)(Name) AS TopOperationsState
FROM clickhousedb.spans_local
GROUP BY TimeBucket, Service;

CREATE TABLE IF NOT EXISTS clickhousedb.service_call_breakdown_external_local ON CLUSTER 'flare_cluster'
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    PeerService LowCardinality(String) CODEC(ZSTD(1)),
    CallCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),
    P50State AggregateFunction(quantile(0.5), UInt64),
    P95State AggregateFunction(quantile(0.95), UInt64)
)
ENGINE = ReplicatedAggregatingMergeTree('/clickhouse/tables/{shard}/clickhousedb/service_call_breakdown_external_local', '{replica}')
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (ServiceName, PeerService, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.service_call_breakdown_external ON CLUSTER 'flare_cluster' AS clickhousedb.service_call_breakdown_external_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'service_call_breakdown_external_local', cityHash64(ServiceName))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_call_breakdown_external_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.service_call_breakdown_external_local
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    SpanAttributes['peer.service'] AS PeerService,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans_local
WHERE SpanAttributes['peer.service'] != ''
GROUP BY TimeBucket, ServiceName, PeerService;

CREATE TABLE IF NOT EXISTS clickhousedb.service_call_breakdown_database_local ON CLUSTER 'flare_cluster'
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
ENGINE = ReplicatedAggregatingMergeTree('/clickhouse/tables/{shard}/clickhousedb/service_call_breakdown_database_local', '{replica}')
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (ServiceName, DbSystem, DbOperation, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.service_call_breakdown_database ON CLUSTER 'flare_cluster' AS clickhousedb.service_call_breakdown_database_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'service_call_breakdown_database_local', cityHash64(ServiceName))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_call_breakdown_database_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.service_call_breakdown_database_local
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
FROM clickhousedb.spans_local
WHERE SpanAttributes['db.system'] != ''
GROUP BY TimeBucket, ServiceName, DbSystem, DbOperation;
