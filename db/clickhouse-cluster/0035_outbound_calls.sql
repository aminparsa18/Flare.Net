-- Flare outbound-calls schema, migration 0035.
--
-- One row per outbound CLIENT call that names a domain and has no `peer.service`: the
-- candidates for the Services Map's external-host leaf nodes (ADR-0072). The Map finds
-- the ones no instrumented span answered by anti-joining this table against spans'
-- `(TraceId, ParentSpanId)`.
--
-- Exists because `spans` is `TraceId`-ordered and 0025's `StartTime` projection has no
-- `Kind`, `Name` or `StatusCode`, so the same query straight over `spans` reads the
-- whole table on every Map poll. This table is `StartTime`-ordered and holds only the
-- columns the query needs. No `ResourceAttributes`: a Map request with filter chips
-- falls back to the live query over `spans`, same as the nodes (ADR-0031).
--
-- The filter and domain are `Flare.Api`'s `ExternalApiQueryBuilder.OutboundCallCondition`
-- and `DomainExpr`, verbatim; a unit test checks this file still contains both. Only
-- spans flushed after this runs are captured - the Map's windows are 24h at most, so the
-- gap closes by itself.
--
-- Cluster variant: sharded by TraceId like `spans`, so a trace's outbound calls sit on
-- the same shard as its child spans.
CREATE TABLE IF NOT EXISTS clickhousedb.outbound_calls_local ON CLUSTER 'flare_cluster'
(
    StartTime DateTime64(9) CODEC(Delta, ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    Domain String CODEC(ZSTD(1)),
    Name LowCardinality(String) CODEC(ZSTD(1)),
    StatusCode Enum8('STATUS_CODE_UNSET' = 0, 'STATUS_CODE_OK' = 1, 'STATUS_CODE_ERROR' = 2),
    DurationNano UInt64 CODEC(ZSTD(1))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/outbound_calls_local', '{replica}')
PARTITION BY toStartOfMonth(StartTime)
ORDER BY StartTime
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.outbound_calls ON CLUSTER 'flare_cluster' AS clickhousedb.outbound_calls_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'outbound_calls_local', cityHash64(TraceId))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.outbound_calls_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.outbound_calls_local
AS
SELECT
    StartTime,
    TraceId,
    SpanId,
    ServiceName,
    multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))) AS Domain,
    Name,
    StatusCode,
    DurationNano
FROM clickhousedb.spans_local
WHERE Kind = 3 AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '' AND SpanAttributes['peer.service'] = '' AND Domain != '';
