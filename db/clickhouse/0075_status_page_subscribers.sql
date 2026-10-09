-- Status pages, migration 0075: notification channels subscribed to a page's incidents.
--
-- `SubscriberChannelIds` holds `notification_channels` ids; an incident opened or updated on the page is
-- sent to each. See `docs-internal/adr/0161-status-page-subscribers.md`.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0074, run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.status_pages ADD COLUMN IF NOT EXISTS SubscriberChannelIds Array(UUID) DEFAULT [];
