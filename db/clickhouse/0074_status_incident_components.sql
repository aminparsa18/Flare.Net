-- Status page incidents, migration 0074: which components an incident affects.
--
-- `Components` holds the `RefId`s (monitor or SLO ids) of the page components the incident touches; empty
-- means it is not tied to a component. See `docs-internal/adr/0160-status-incident-components.md`.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0073, run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.status_incidents ADD COLUMN IF NOT EXISTS Components Array(UUID) DEFAULT [];
