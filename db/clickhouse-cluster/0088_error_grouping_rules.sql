-- Custom error grouping rules, migration 0088 - CLUSTER VARIANT. Same table and rationale as
-- db/clickhouse/0088_error_grouping_rules.sql: the usual _local + Distributed pair.
CREATE TABLE IF NOT EXISTS clickhousedb.error_grouping_rules_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    IsDeleted UInt8 DEFAULT 0,
    Position UInt32,
    Kind LowCardinality(String),
    ExceptionType String CODEC(ZSTD(1)),
    Pattern String CODEC(ZSTD(1)),
    Replacement String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/error_grouping_rules_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.error_grouping_rules ON CLUSTER 'flare_cluster' AS clickhousedb.error_grouping_rules_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'error_grouping_rules_local', rand())
SETTINGS insert_distributed_sync = 1;
