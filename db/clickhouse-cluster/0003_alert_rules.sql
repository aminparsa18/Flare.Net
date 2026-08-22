-- Alerting schema, migration 0003 - CLUSTER VARIANT.
--
-- Same columns/CRUD-via-tombstone rationale as db/clickhouse/0003_alert_rules.sql.
-- `ReplicatedReplacingMergeTree` (not plain `ReplicatedMergeTree`) is the Replicated
-- counterpart of the single-node `ReplacingMergeTree(UpdatedAt)` engine, keeping the
-- same "reads go through `FINAL WHERE IsDeleted = 0`" semantics.
--
-- Confirmed live (2026-08-22) against a real 4-node cluster: `SELECT ... FROM
-- alert_rules FINAL` correctly collapses a create-then-update pair down to exactly one
-- row (the latest UpdatedAt), read consistently from every node regardless of which
-- shard the query lands on - `Distributed` + `FINAL` over `ReplicatedReplacingMergeTree`
-- is not a problem in practice, despite being a less-common combination. The reviewed
-- non-`FINAL` fallback named in the single-node file's own comments (`GROUP BY Id ORDER
-- BY UpdatedAt DESC LIMIT 1 BY Id HAVING IsDeleted = 0`) remains available if `FINAL`'s
-- per-part merge cost ever becomes measurably worse than that alternative at scale, but
-- isn't needed for correctness.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,
    ConditionJson String CODEC(ZSTD(1)),
    ThresholdCount UInt64,
    ThresholdComparator LowCardinality(String),
    WindowSeconds UInt32,
    CooldownSeconds UInt32,
    WebhookUrl String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/alert_rules_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.alert_rules ON CLUSTER 'flare_cluster' AS clickhousedb.alert_rules_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'alert_rules_local', rand())
SETTINGS insert_distributed_sync = 1;
