-- Status pages schema, migration 0072.
--
-- `status_pages` (Flare.Api.Model.StatusPageModels.cs): a public, read-only page of service health. A page
-- is a title, a URL slug and a list of components, each backed by a synthetic monitor (ADR-0128) or an SLO
-- (ADR-0108). `GET /api/public/status/{slug}` serves the enabled ones without a session. See
-- `docs-internal/adr/0158-status-pages.md`. Same CRUD-via-tombstone shape as `maintenance_windows`
-- (migration 0031) and `alert_templates` (0070), and for the same reason.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0071, run this by hand via `clickhouse-client`
-- against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.status_pages
(
    Id UUID,
    Slug String CODEC(ZSTD(1)),
    Title String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 0,

    -- JSON array of { name, kind: 'Monitor' | 'Slo', refId }.
    ComponentsJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
