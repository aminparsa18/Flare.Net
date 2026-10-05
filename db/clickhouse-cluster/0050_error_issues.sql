-- Error issue lifecycle schema, migration 0050 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0050_error_issues.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed`, same shape as maintenance_windows' (migration
-- 0031's cluster file).
CREATE TABLE IF NOT EXISTS clickhousedb.error_issues_local ON CLUSTER 'flare_cluster'
(
    Id String CODEC(ZSTD(1)),
    ExceptionType String CODEC(ZSTD(1)),
    ExceptionMessage String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Status LowCardinality(String),
    Assignee String CODEC(ZSTD(1)),
    StatusChangedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    StatusChangedBy String CODEC(ZSTD(1)),
    IgnoreUntil Nullable(DateTime64(3)),
    IgnoreUntilOccurrences Nullable(UInt32),
    KnownVersions Array(String),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/error_issues_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.error_issues ON CLUSTER 'flare_cluster' AS clickhousedb.error_issues_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'error_issues_local', rand())
SETTINGS insert_distributed_sync = 1;
