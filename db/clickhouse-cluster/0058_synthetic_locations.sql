-- Synthetic probe locations, migration 0058 (cluster variant). See db/clickhouse/0058_synthetic_locations.sql.
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Locations Array(String) DEFAULT [] CODEC(ZSTD(1));

ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Locations Array(String) DEFAULT [] CODEC(ZSTD(1));
