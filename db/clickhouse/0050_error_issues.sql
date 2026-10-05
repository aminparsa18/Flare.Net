-- Error issue lifecycle schema, migration 0050.
--
-- Per-exception-group state for the /errors page (resolve / ignore / assign) - see
-- `docs-internal/adr/0121-error-issue-lifecycle.md`.
--
-- `error_issues` (Flare.Api.Model.ErrorIssueModels.cs): one row per (exception.type,
-- exception.message) group that has been triaged, keyed by `Id` = the group fingerprint
-- (`Flare.Api.Errors.ErrorIssueFingerprint`). A group nobody has touched has no row and reads as
-- "Open". Same CRUD-via-tombstone `ReplacingMergeTree(UpdatedAt)` shape as `maintenance_windows`
-- (migration 0031) and `slos` (migration 0049), for the same reason: a few rows per instance,
-- edited rarely, read through `LatestVersionSql`. Returning a group to Open with no assignee
-- writes a tombstone rather than storing an empty row.
--
-- `Status` is the *stored* decision: "Open" | "Resolved" | "Ignored". "Regressed" is never
-- stored - it is derived at read time from `spans` (a resolved group recurring in a service.version
-- not in `KnownVersions`), as is an ignore's expiry by occurrence count.
--
-- `KnownVersions`: the distinct `service.version` values the group had been seen in when it was
-- resolved, so a later occurrence in any other version reads as a regression. `StatusChangedAt`
-- is the instant occurrences are counted from (regression evidence, "ignore for N more").
CREATE TABLE IF NOT EXISTS clickhousedb.error_issues
(
    Id String CODEC(ZSTD(1)),
    ExceptionType String CODEC(ZSTD(1)),
    ExceptionMessage String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Status LowCardinality(String),
    Assignee String CODEC(ZSTD(1)),
    StatusChangedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    StatusChangedBy String CODEC(ZSTD(1)),

    -- Ignored only: the ignore lapses at this instant, and/or after this many further
    -- occurrences. Both NULL = ignored until someone reopens it.
    IgnoreUntil Nullable(DateTime64(3)),
    IgnoreUntilOccurrences Nullable(UInt32),

    KnownVersions Array(String),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
