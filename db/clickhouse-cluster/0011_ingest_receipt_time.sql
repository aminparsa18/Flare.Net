-- Adds Flare.Ingest's own receipt-time column to every OTLP signal table, migration
-- 0011 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0011_ingest_receipt_time.sql - see that file
-- for the full explanation of IngestedAt vs ObservedTimestamp and ADR-0014 for the
-- design decision. Same `_local` + Distributed-keeps-the-name pattern as every other
-- cluster-variant migration (0002_logs_event_id.sql's cluster variant explains why both
-- need the same ADD COLUMN: a Distributed table's column set doesn't auto-sync from its
-- underlying local table).
ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.logs ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));

ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.spans ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));

ALTER TABLE clickhousedb.metrics_gauge_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.metrics_gauge ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));

ALTER TABLE clickhousedb.metrics_sum_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.metrics_sum ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));

ALTER TABLE clickhousedb.metrics_histogram_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.metrics_histogram ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
