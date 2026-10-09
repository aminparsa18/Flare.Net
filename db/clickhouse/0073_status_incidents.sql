-- Status page incidents schema, migration 0073.
--
-- `status_incidents` (Flare.Api.Model.StatusPageModels.cs): a written incident on a status page (ADR-0158),
-- with its timeline of updates. One row per incident version; the updates are a JSON array so posting an
-- update is one INSERT. `PageId` never changes across versions, so reads can filter on it before picking
-- the latest version. See `docs-internal/adr/0159-status-page-incidents.md`. Same CRUD-via-tombstone shape
-- as `status_pages` (migration 0072).
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0072, run this by hand via `clickhouse-client`
-- against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.status_incidents
(
    Id UUID,
    PageId UUID,
    Title String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- JSON array of { at, status: 'Investigating' | 'Identified' | 'Monitoring' | 'Resolved', message }, oldest first.
    UpdatesJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
