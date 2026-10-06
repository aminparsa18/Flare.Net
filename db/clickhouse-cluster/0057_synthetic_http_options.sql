-- Same columns/rationale as db/clickhouse/0057_synthetic_http_options.sql.
-- Cluster variant: ALTER the `_local` table on every node; the Distributed table needs the columns too.
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS RequestHeaders String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS RequestBody String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS BodyContains String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS BodyNotContains String DEFAULT '' CODEC(ZSTD(1));

ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS RequestHeaders String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS RequestBody String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS BodyContains String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS BodyNotContains String DEFAULT '' CODEC(ZSTD(1));
