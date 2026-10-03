-- Metric attribute rules schema, migration 0041.
--
-- Storage for per-metric attribute reduction at ingest: a rule names a metric (exact
-- name, or a prefix ending in `*`) and either a set of data-point attributes to drop or
-- a set to keep (dropping all others), applied by `Flare.Ingest` at flush time (see
-- `Flare.Ingest.Pipeline.MetricRules.MetricAttributeReducer`). `Flare.Api` owns writes,
-- `Flare.Ingest` polls this table read-only. See
-- docs-internal/adr/0083-metric-attribute-reduction.md.
--
-- Same "config, not telemetry" CRUD-via-tombstone shape as `pipeline_rules` (migration
-- 0024): every create/update INSERTs a new version of the `Id` keyed by `UpdatedAt`,
-- delete INSERTs a version with `IsDeleted = 1`, and reads take the latest version per
-- `Id` (`LatestVersionSql`, not `FINAL` - see ADR-0074).
CREATE TABLE IF NOT EXISTS clickhousedb.metric_attribute_rules
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,

    -- Exact metric name, or a prefix followed by a single trailing `*`.
    MetricName String CODEC(ZSTD(1)),

    -- 0 = Drop (remove the listed attributes), 1 = KeepOnly (remove all others).
    Mode UInt8,

    -- JSON array of attribute keys.
    AttributesJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
