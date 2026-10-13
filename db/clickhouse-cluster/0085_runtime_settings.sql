-- Runtime settings, migration 0085 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0085_runtime_settings.sql; Replicated
-- counterpart of the single-node ReplacingMergeTree, same shape as `telemetry_exports` (0071). Reads use
-- `LIMIT 1 BY Id` rather than `FINAL` because the Distributed table shards by `rand()` (ADR-0074).
CREATE TABLE IF NOT EXISTS clickhousedb.runtime_settings_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Key LowCardinality(String),
    ValueJson String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    UpdatedBy String CODEC(ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/runtime_settings_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.runtime_settings ON CLUSTER 'flare_cluster' AS clickhousedb.runtime_settings_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'runtime_settings_local', rand())
SETTINGS insert_distributed_sync = 1;
