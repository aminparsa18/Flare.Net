-- OTLP dropped-item counts, migration 0091 (see docs-internal/adr/0272-store-otlp-dropped-counts.md).
--
-- An SDK that hits an `OTEL_*_COUNT_LIMIT` (attributes per span/log/event/link, events per span, links per span) discards
-- the surplus and reports how many it discarded: `dropped_attributes_count` on spans and log records,
-- `dropped_events_count` and `dropped_links_count` on spans. Flare stored none of them, so a span that lost half its
-- attributes looked complete. These columns keep the counts so the span and event detail views can say "N attributes
-- dropped".
--
-- `UInt32 DEFAULT 0`: rows from before this migration, and spans/logs from an SDK that sets no limit, read 0 ("nothing
-- known to be dropped"). Not added to any ORDER BY or materialized view; they are only read back with the row.
ALTER TABLE clickhousedb.spans
    ADD COLUMN IF NOT EXISTS DroppedAttributesCount UInt32 DEFAULT 0 CODEC(ZSTD(1)),
    ADD COLUMN IF NOT EXISTS DroppedEventsCount UInt32 DEFAULT 0 CODEC(ZSTD(1)),
    ADD COLUMN IF NOT EXISTS DroppedLinksCount UInt32 DEFAULT 0 CODEC(ZSTD(1));
ALTER TABLE clickhousedb.logs
    ADD COLUMN IF NOT EXISTS DroppedAttributesCount UInt32 DEFAULT 0 CODEC(ZSTD(1));
