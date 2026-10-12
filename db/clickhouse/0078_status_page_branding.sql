-- Status page custom domain and branding, migration 0078.
--
-- `status_pages.Domain` (the host the page is also served on, e.g. status.acme.com; empty = none),
-- `LogoUrl`, `AccentColor` (#rrggbb) and `SupportUrl` (https or mailto): see
-- `docs-internal/adr/0165-status-page-domain-and-branding.md`.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0077, run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.status_pages ADD COLUMN IF NOT EXISTS Domain String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.status_pages ADD COLUMN IF NOT EXISTS LogoUrl String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.status_pages ADD COLUMN IF NOT EXISTS AccentColor String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.status_pages ADD COLUMN IF NOT EXISTS SupportUrl String DEFAULT '' CODEC(ZSTD(1));
