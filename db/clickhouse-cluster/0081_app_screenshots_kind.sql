-- Scheduled session screenshots, migration 0081 - CLUSTER VARIANT. Same column/rationale as
-- db/clickhouse/0081_app_screenshots_kind.sql, added to the replicated storage table and the Distributed one
-- (same pair as migration 0079).
ALTER TABLE clickhousedb.app_screenshots_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Kind LowCardinality(String) DEFAULT 'error' CODEC(ZSTD(1));

ALTER TABLE clickhousedb.app_screenshots ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Kind LowCardinality(String) DEFAULT 'error';
