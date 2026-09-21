-- Pipeline rules schema, migration 0024 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0024_pipeline_rules.sql.
-- `ReplicatedReplacingMergeTree` (not plain `ReplicatedMergeTree`) is the Replicated
-- counterpart of the single-node `ReplacingMergeTree(UpdatedAt)` engine, keeping the
-- same "reads go through `FINAL WHERE IsDeleted = 0`" semantics - see
-- `alert_rules`'s cluster variant (migration 0003) for the live-cluster confirmation
-- that `FINAL` over `ReplicatedReplacingMergeTree` collapses correctly regardless of
-- which node/shard the query lands on.
CREATE TABLE IF NOT EXISTS clickhousedb.pipeline_rules_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,
    ConditionJson String CODEC(ZSTD(1)),
    ActionsJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/pipeline_rules_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.pipeline_rules ON CLUSTER 'flare_cluster' AS clickhousedb.pipeline_rules_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'pipeline_rules_local', rand())
SETTINGS insert_distributed_sync = 1;
