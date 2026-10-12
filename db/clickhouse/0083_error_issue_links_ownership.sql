-- Error issue link + ownership rules, migration 0083 (see docs-internal/adr/0195-error-issue-links-ownership.md).
--
-- `error_issues.IssueUrl`: the external tracker issue (GitHub, GitLab, ...) a group is linked to; '' = none.
-- A group with only a link and no status change is still a stored row (Open + unassigned + link).
--
-- `error_ownership_rules`: ordered "who owns errors like this" rules. `Kind` is 'Service' (matches a service
-- that recorded the group) or 'ExceptionType'; `Pattern` is a case-insensitive glob where `*` matches any run
-- of characters; `Owner` is a username. The first matching rule by `Position` is the group's owner unless the
-- group has an explicit assignee. Same CRUD-via-tombstone `ReplacingMergeTree(UpdatedAt)` shape as
-- `error_issues` (migration 0050), read through `LatestVersionSql`.
--
-- Existing numbered migrations are immutable once merged, hence a new file. Like migrations 0002-0082, run
-- this by hand via `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.error_issues ADD COLUMN IF NOT EXISTS IssueUrl String DEFAULT '' CODEC(ZSTD(1));

CREATE TABLE IF NOT EXISTS clickhousedb.error_ownership_rules
(
    Id UUID,
    IsDeleted UInt8 DEFAULT 0,
    Position UInt32,
    Kind LowCardinality(String),
    Pattern String CODEC(ZSTD(1)),
    Owner String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
