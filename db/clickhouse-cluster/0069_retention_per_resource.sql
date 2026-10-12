-- Per-resource retention, migration 0069 - CLUSTER VARIANT. Same columns/rationale as
-- db/clickhouse/0069_retention_per_resource.sql, added to each replicated `_local` table and to
-- its Distributed counterpart (the Distributed table's own DEFAULT is what fills the column for
-- inserts that go through it, so both must carry it - same pair as migration 0058).
ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.logs ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.spans ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_gauge_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_gauge ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_sum_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_sum ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_histogram_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_histogram ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_exponential_histogram_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.metrics_exponential_histogram ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.profile_samples_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.profile_samples ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

ALTER TABLE clickhousedb.retention_operations_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS RulesJson String DEFAULT '' CODEC(ZSTD(1));

ALTER TABLE clickhousedb.retention_operations ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS RulesJson String DEFAULT '' CODEC(ZSTD(1));
