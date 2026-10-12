-- Release markers, migration 0080.
--
-- `releases` (Flare.Api.Model.ReleaseModels.cs): one row per (service, version) a CI pipeline or a
-- person has marked as deployed - version, commit, link and deploy time - so the Releases page can
-- show which exception groups each version introduced. See
-- `docs-internal/adr/0182-release-tracking.md`.
--
-- Same CRUD-via-tombstone `ReplacingMergeTree(UpdatedAt)` shape as `error_issues` (migration 0050):
-- a handful of rows per service, written by a deploy step, read through `LatestVersionSql`. `Id` is a
-- hash of (ServiceName, Version) (`Flare.Api.Query.ReleaseQueryBuilder.ComputeId`), so re-marking the
-- same version updates it instead of adding a second one.
CREATE TABLE IF NOT EXISTS clickhousedb.releases
(
    Id String CODEC(ZSTD(1)),
    ServiceName String CODEC(ZSTD(1)),
    Version String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    CommitSha String CODEC(ZSTD(1)),
    Url String CODEC(ZSTD(1)),
    Notes String CODEC(ZSTD(1)),
    DeployedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    CreatedBy String CODEC(ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
