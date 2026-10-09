-- Telemetry export settings, migration 0071.
--
-- Managed (dashboard/API) configuration for the two ways Flare copies telemetry out
-- (docs-internal/adr/0157-managed-telemetry-export.md):
--   Kind 0 = OTLP forwarding target (many rows; Flare.Ingest reads them, ADR-0155)
--   Kind 1 = S3 archive settings    (one row, fixed Id; Flare.AlertWorker reads it, ADR-0156)
-- `ConfigJson` holds the kind-specific settings, including credentials (headers, S3 keys); Flare.Api
-- masks them in every response. Kept as one JSON column rather than a column per setting because the
-- two kinds share nothing but the envelope and the settings grow with each feature.
--
-- Same "config, not telemetry" CRUD-via-tombstone shape as `metric_attribute_rules` (migration 0041):
-- every create/update INSERTs a new version of the `Id` keyed by `UpdatedAt`, delete INSERTs a version
-- with `IsDeleted = 1`, and reads take the latest version per `Id` (`LatestVersionSql`, ADR-0074).
CREATE TABLE IF NOT EXISTS clickhousedb.telemetry_exports
(
    Id UUID,
    Kind UInt8,
    Name String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,
    ConfigJson String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
