-- Dashboards schema, migration 0043.
--
-- Free-form tags on a dashboard (filter chips on the Dashboards page) - see
-- `docs-internal/adr/0089-dashboard-tags-and-pins.md`. `Tags` is part of the dashboard's own
-- versioned row, so an update writes the new tag list with the rest of the row. `[]` - the
-- default, so every pre-existing dashboard - means no tags.
--
-- Per-user pins are not stored here: they live in Identity's SQLite (migration 0022).
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0042, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.dashboards
    ADD COLUMN IF NOT EXISTS Tags Array(String) DEFAULT [] AFTER Description;
