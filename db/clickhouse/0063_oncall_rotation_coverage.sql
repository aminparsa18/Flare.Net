-- Alerting schema, migration 0063.
--
-- On-call rotation coverage windows - see `docs-internal/adr/0139-oncall-rotation-coverage.md`.
--
-- `oncall_rotations.Coverage`: a JSON object `{timeZone, days, startMinute, endMinute}` (see
-- Flare.Api.Model.OnCallCoverage) limiting when the rotation pages, or `''` (every existing
-- rotation) for always. A JSON string for the same reason as `Overrides` (0062): read and written
-- whole with the rotation, never filtered in SQL.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0062, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.oncall_rotations
    ADD COLUMN IF NOT EXISTS Coverage String DEFAULT '' CODEC(ZSTD(1));
