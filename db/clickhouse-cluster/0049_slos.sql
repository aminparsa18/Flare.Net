-- SLO schema, migration 0049 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0049_slos.sql (including its manual backfill
-- note - run it against `spans`/`span_sli_minute` the same way, the Distributed tables
-- route it). `ReplicatedAggregatingMergeTree` + `Distributed` for the pre-aggregate, whose
-- materialized view reads `spans_local` and writes the `_local` table, same shape as
-- service_metrics' cluster variant (migration 0022's cluster file); `ReplicatedReplacingMergeTree`
-- + `Distributed` for `slos`, same shape as maintenance_windows' (migration 0031's cluster file).
CREATE TABLE IF NOT EXISTS clickhousedb.span_sli_minute_local ON CLUSTER 'flare_cluster'
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
ENGINE = ReplicatedAggregatingMergeTree('/clickhouse/tables/{shard}/clickhousedb/span_sli_minute_local', '{replica}')
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (ServiceName, Name, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.span_sli_minute ON CLUSTER 'flare_cluster' AS clickhousedb.span_sli_minute_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'span_sli_minute_local', cityHash64(ServiceName))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.span_sli_minute_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.span_sli_minute_local
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
FROM clickhousedb.spans_local
WHERE Kind IN (2, 5) OR ParentSpanId = ''
GROUP BY TimeBucket, ServiceName, Name;

CREATE TABLE IF NOT EXISTS clickhousedb.slos_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Kind LowCardinality(String),
    ServiceName String CODEC(ZSTD(1)),
    OperationName String CODEC(ZSTD(1)),
    TargetPercent Float64,
    LatencyThresholdMs UInt32,
    WindowDays UInt16,
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/slos_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.slos ON CLUSTER 'flare_cluster' AS clickhousedb.slos_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'slos_local', rand())
SETTINGS insert_distributed_sync = 1;

ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SloConditionJson String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SloConditionJson String DEFAULT '' CODEC(ZSTD(1));
