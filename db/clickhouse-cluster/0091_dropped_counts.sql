-- OTLP dropped-item counts, migration 0091 - CLUSTER VARIANT.
--
-- Same columns as db/clickhouse/0091_dropped_counts.sql. Same `_local` + Distributed pairing as every other
-- cluster-variant migration: both need the ADD COLUMN.
ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS DroppedAttributesCount UInt32 DEFAULT 0 CODEC(ZSTD(1)),
    ADD COLUMN IF NOT EXISTS DroppedEventsCount UInt32 DEFAULT 0 CODEC(ZSTD(1)),
    ADD COLUMN IF NOT EXISTS DroppedLinksCount UInt32 DEFAULT 0 CODEC(ZSTD(1));
ALTER TABLE clickhousedb.spans ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS DroppedAttributesCount UInt32 DEFAULT 0 CODEC(ZSTD(1)),
    ADD COLUMN IF NOT EXISTS DroppedEventsCount UInt32 DEFAULT 0 CODEC(ZSTD(1)),
    ADD COLUMN IF NOT EXISTS DroppedLinksCount UInt32 DEFAULT 0 CODEC(ZSTD(1));
ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS DroppedAttributesCount UInt32 DEFAULT 0 CODEC(ZSTD(1));
ALTER TABLE clickhousedb.logs ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS DroppedAttributesCount UInt32 DEFAULT 0 CODEC(ZSTD(1));
