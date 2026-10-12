-- Data-subject erasure operations, migration 0084, cluster variant (see db/clickhouse/0084_erasure_operations.sql).
CREATE TABLE IF NOT EXISTS clickhousedb.erasure_operations_local ON CLUSTER 'flare_cluster'
(
    TransactionId UUID,
    Status LowCardinality(String),
    Error String CODEC(ZSTD(1)),
    IdentifierKeys Array(String) CODEC(ZSTD(1)),
    SubjectHash String CODEC(ZSTD(1)),
    CountsJson String CODEC(ZSTD(1)),
    RequestedBy String CODEC(ZSTD(1)),
    RequestedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/erasure_operations_local', '{replica}', UpdatedAt)
ORDER BY (TransactionId)
TTL toDateTime(RequestedAt) + INTERVAL 1095 DAY
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.erasure_operations ON CLUSTER 'flare_cluster' AS clickhousedb.erasure_operations_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'erasure_operations_local', rand())
SETTINGS insert_distributed_sync = 1;
