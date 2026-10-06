-- Synthetic regex and JSON-path assertions, migration 0059 (cluster variant). See db/clickhouse/0059_synthetic_regex_jsonpath.sql.
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS BodyMatchesRegex String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS JsonPath String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS JsonPathEquals String DEFAULT '' CODEC(ZSTD(1));

ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS BodyMatchesRegex String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS JsonPath String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS JsonPathEquals String DEFAULT '' CODEC(ZSTD(1));
