-- Synthetic monitoring schema, migration 0056 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0056_synthetic_monitors.sql.
-- `ReplicatedReplacingMergeTree` + `Distributed`, same shape as oncall_rotations' cluster variant (0055).
CREATE TABLE IF NOT EXISTS clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 1,
    Kind LowCardinality(String),
    Target String CODEC(ZSTD(1)),
    Method LowCardinality(String) DEFAULT 'GET',
    ExpectedStatus UInt16 DEFAULT 0,
    IntervalSeconds UInt32,
    TimeoutSeconds UInt32,
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/synthetic_monitors_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' AS clickhousedb.synthetic_monitors_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'synthetic_monitors_local', rand())
SETTINGS insert_distributed_sync = 1;
