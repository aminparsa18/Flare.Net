-- Project ownership on config objects, migration 0052 (ADR-0123 phase 3).
--
-- `ProjectId` ties a dashboard, saved view, alert rule or SLO to a project (Identity's
-- `Projects` table). NULL - the default, so every pre-existing row - means instance-wide: visible
-- to everyone and editable under the usual role rules. A set ProjectId makes the object visible
-- only to that project's members and editable only by members with the Admin/Member project role.
--
-- Part of each table's own versioned row, so a move between projects is an ordinary update.
-- There is no foreign key (the project lives in a different store); an object whose project was
-- deleted stays hidden from non-admins until an admin reassigns it.
--
-- Existing numbered migrations are immutable once merged, hence a new file. Like migrations
-- 0002-0051, run this by hand via `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.dashboards
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.saved_views
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.slos
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
