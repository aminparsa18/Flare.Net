-- Custom error grouping rules, migration 0088 (docs-internal/adr/0259-error-grouping-rules.md).
--
-- `error_grouping_rules`: the ordered rules that refine how exception events are grouped on `/errors`, applied after
-- the built-in message normalization (ADR-0258). `Kind` is 'Replace' (`Pattern` is an RE2 regular expression and
-- `Replacement` what each match becomes, `\1` for a capture group; `ExceptionType` unused) or 'Merge' (`ExceptionType` is
-- the exact exception type, `Pattern` the group message to fold away and `Replacement` the group message it joins).
-- A save replaces the whole list as a unit, so `Id` is internal and the order is `Position`. Same CRUD-via-tombstone
-- `ReplacingMergeTree(UpdatedAt)` shape as `error_ownership_rules` (migration 0083), read through `LatestVersionSql`.
--
-- The switch that turns the built-in normalization off lives in `runtime_settings` (migration 0085), not here.
--
-- Existing numbered migrations are immutable once merged, hence a new file. Like migrations 0002-0087, run this by
-- hand via `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.error_grouping_rules
(
    Id UUID,
    IsDeleted UInt8 DEFAULT 0,
    Position UInt32,
    Kind LowCardinality(String),
    ExceptionType String CODEC(ZSTD(1)),
    Pattern String CODEC(ZSTD(1)),
    Replacement String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
