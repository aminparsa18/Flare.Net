-- Service-metrics schema, migration 0022 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0022_service_metrics.sql (see ADR-0030), same
-- `_local` + `Replicated<Engine>` + `Distributed` wrapper pattern as `spans`/`spans_local`
-- (0007_spans.sql's own cluster variant).
--
-- The materialized view attaches to `spans_local` and writes to `service_metrics_local` -
-- it fires per-shard, on each node's own local inserts, the standard MV-on-Distributed
-- placement (a materialized view attached to a `Distributed` table does not work the same
-- way; it must sit on the underlying local table on each node). `spans` shards by
-- `cityHash64(TraceId)`, not by service, so the same `ServiceName` legitimately ends up
-- with partial aggregate states on more than one shard - exactly what
-- `AggregatingMergeTree` + `quantileMerge()` through the `Distributed` `service_metrics`
-- table at query time is for; no additional handling needed here.
CREATE TABLE IF NOT EXISTS clickhousedb.service_metrics_local ON CLUSTER 'flare_cluster'
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    RequestCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),
    P50State AggregateFunction(quantile(0.5), UInt64),
    P95State AggregateFunction(quantile(0.95), UInt64),
    P99State AggregateFunction(quantile(0.99), UInt64)
)
ENGINE = ReplicatedAggregatingMergeTree('/clickhouse/tables/{shard}/clickhousedb/service_metrics_local', '{replica}')
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (ServiceName, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.service_metrics ON CLUSTER 'flare_cluster' AS clickhousedb.service_metrics_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'service_metrics_local', cityHash64(ServiceName))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.service_metrics_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.service_metrics_local
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    count() AS RequestCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State,
    quantileState(0.99)(DurationNano) AS P99State
FROM clickhousedb.spans_local
WHERE ParentSpanId = ''
GROUP BY TimeBucket, ServiceName;
