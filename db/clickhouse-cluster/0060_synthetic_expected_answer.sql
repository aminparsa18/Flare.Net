-- Synthetic DNS/UDP/ICMP probes, migration 0060 (cluster variant). See db/clickhouse/0060_synthetic_expected_answer.sql.
ALTER TABLE clickhousedb.synthetic_monitors_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS ExpectedAnswer String DEFAULT '' CODEC(ZSTD(1));

ALTER TABLE clickhousedb.synthetic_monitors ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS ExpectedAnswer String DEFAULT '' CODEC(ZSTD(1));
