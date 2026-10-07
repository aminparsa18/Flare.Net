-- Retention operations, migration 0068 - CLUSTER VARIANT. Same column/rationale as
-- db/clickhouse/0068_retention_cold_after.sql, added to the replicated storage table and the
-- Distributed one (same pair as migration 0058).
ALTER TABLE clickhousedb.retention_operations_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS ColdAfterDays UInt32 DEFAULT 0;

ALTER TABLE clickhousedb.retention_operations ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS ColdAfterDays UInt32 DEFAULT 0;
