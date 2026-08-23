-- Flare span storage schema, migration 0007 - CLUSTER VARIANT.
--
-- Same table/columns/indices/PARTITION BY/ORDER BY as db/clickhouse/0007_spans.sql (see
-- that file for the full rationale). Same `_local` (ReplicatedMergeTree) +
-- Distributed-keeps-the-name pattern as logs (0001_logs.sql's cluster variant).
CREATE TABLE IF NOT EXISTS clickhousedb.spans_local ON CLUSTER 'flare_cluster'
(
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    ParentSpanId String CODEC(ZSTD(1)),
    TraceState String CODEC(ZSTD(1)),
    Name LowCardinality(String) CODEC(ZSTD(1)),
    Kind UInt8,
    StartTime DateTime64(9) CODEC(Delta, ZSTD(1)),
    EndTime DateTime64(9) CODEC(Delta, ZSTD(1)),
    DurationNano UInt64 CODEC(Delta, ZSTD(1)),
    StatusCode Enum8('STATUS_CODE_UNSET' = 0, 'STATUS_CODE_OK' = 1, 'STATUS_CODE_ERROR' = 2),
    StatusMessage String CODEC(ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    ResourceSchemaUrl String CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    ScopeSchemaUrl String CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    ScopeAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    SpanAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    Events Nested
    (
        TimeUnixNano DateTime64(9),
        Name LowCardinality(String),
        Attributes Map(LowCardinality(String), String)
    ),

    INDEX idx_service ServiceName TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_span_attr_key mapKeys(SpanAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_span_attr_value mapValues(SpanAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_key mapKeys(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_value mapValues(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/spans_local', '{replica}')
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (TraceId, StartTime, SpanId)
SETTINGS index_granularity = 8192;

-- cityHash64(TraceId) sharding - not rand() - so every span of a given trace lands on
-- the same shard, matching the single-node table's own TraceId-leading ORDER BY
-- adjacency. Was rand() originally (see docs/clustering.md's "Design decision" section):
-- even distribution, but a single trace's spans could scatter across shards, a real cost
-- for "get full trace by id" queries. cityHash64 is ClickHouse's standard sharding-key
-- hash - still an even distribution across shards (traces, not spans, are the unit being
-- distributed now), just no longer independent per span.
--
-- Schema-level co-location only, as of this writing - Flare.Api's actual trace-by-id
-- query doesn't yet set optimize_skip_unused_shards to have ClickHouse skip the shard
-- that provably can't hold a given TraceId; it still fans out to both and merges, just
-- against physically adjacent data on whichever shard answers. Skip-shard pruning is a
-- distinct, not-yet-verified follow-up (see docs/clustering.md).
CREATE TABLE IF NOT EXISTS clickhousedb.spans ON CLUSTER 'flare_cluster' AS clickhousedb.spans_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'spans_local', cityHash64(TraceId))
SETTINGS insert_distributed_sync = 1;
