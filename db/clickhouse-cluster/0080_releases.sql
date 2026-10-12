-- Release markers, migration 0080 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0080_releases.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed`, same shape as error_issues' (migration 0050's
-- cluster file).
CREATE TABLE IF NOT EXISTS clickhousedb.releases_local ON CLUSTER 'flare_cluster'
(
    Id String CODEC(ZSTD(1)),
    ServiceName String CODEC(ZSTD(1)),
    Version String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    CommitSha String CODEC(ZSTD(1)),
    Url String CODEC(ZSTD(1)),
    Notes String CODEC(ZSTD(1)),
    DeployedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    CreatedBy String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/releases_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.releases ON CLUSTER 'flare_cluster' AS clickhousedb.releases_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'releases_local', rand())
SETTINGS insert_distributed_sync = 1;
