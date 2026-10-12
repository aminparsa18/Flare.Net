-- Error issue link + ownership rules, migration 0083 - CLUSTER VARIANT. Same column/table and rationale as
-- db/clickhouse/0083_error_issue_links_ownership.sql: the column goes on the replicated storage table and the
-- Distributed one (same pair as migration 0081); the rules table is the usual _local + Distributed pair.
ALTER TABLE clickhousedb.error_issues_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IssueUrl String DEFAULT '' CODEC(ZSTD(1));

ALTER TABLE clickhousedb.error_issues ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IssueUrl String DEFAULT '';

CREATE TABLE IF NOT EXISTS clickhousedb.error_ownership_rules_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    IsDeleted UInt8 DEFAULT 0,
    Position UInt32,
    Kind LowCardinality(String),
    Pattern String CODEC(ZSTD(1)),
    Owner String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/error_ownership_rules_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.error_ownership_rules ON CLUSTER 'flare_cluster' AS clickhousedb.error_ownership_rules_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'error_ownership_rules_local', rand())
SETTINGS insert_distributed_sync = 1;
