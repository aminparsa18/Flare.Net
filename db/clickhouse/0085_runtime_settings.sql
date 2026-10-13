-- Runtime settings, migration 0085 (docs-internal/adr/0210-runtime-settings-pii-toggle.md).
--
-- Deployment-wide switches an Admin flips from the dashboard and `Flare.Ingest` picks up without a restart.
-- Today the only key is `pii.enabled` (managed PII redaction, ADR-0199). `Flare.Api` writes, `Flare.Ingest`
-- polls this table read-only on `PipelineRuleOptions.RefreshInterval`, same split as `pipeline_rules` (0024).
--
-- One row per `Key`, `Id` is a fixed UUID per key so it fits the shared `LatestVersionSql` shape. Same
-- CRUD-via-tombstone `ReplacingMergeTree(UpdatedAt)` as `telemetry_exports` (0071): every save INSERTs a newer
-- version, "revert to configuration" INSERTs a tombstone (`IsDeleted = 1`), and reads pick the latest version
-- with `ORDER BY UpdatedAt DESC LIMIT 1 BY Id`, never `FINAL`.
--
-- `ValueJson` is the setting's value as JSON (`true`/`false` for `pii.enabled`), opaque to the table.
-- `UpdatedBy` is the username that made the change; the audit log (ADR-0079) is the full history.
--
-- Existing numbered migrations are immutable once merged, hence a new file. Like migrations 0002-0084, run
-- this by hand via `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.runtime_settings
(
    Id UUID,
    Key LowCardinality(String),
    ValueJson String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    UpdatedBy String CODEC(ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
