-- Alerting schema, migration 0062.
--
-- On-call rotation overrides - see `docs-internal/adr/0137-oncall-rotation-overrides.md`.
--
-- `oncall_rotations.Overrides`: a JSON array of `{startsAt, endsAt, channelId}` objects (see
-- Flare.Api.Model.OnCallOverride). While one covers the current instant, that channel is on call
-- instead of the scheduled participant ("swap Tuesday"). A JSON string rather than parallel
-- arrays: overrides are read and written whole with the rotation and never filtered in SQL.
-- `'[]'` (every existing rotation) means none.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0061, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.oncall_rotations
    ADD COLUMN IF NOT EXISTS Overrides String DEFAULT '[]' CODEC(ZSTD(1));
