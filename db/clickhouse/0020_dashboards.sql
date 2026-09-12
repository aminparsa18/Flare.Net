-- Dashboards schema, migration 0020.
--
-- Storage for the "Custom, user-built dashboards" roadmap item: a named, multi-panel
-- object composed from arbitrary log/trace/metric queries, CRUD'd via Flare.Api's
-- `/api/dashboards` endpoints. See docs-internal/adr/0023-custom-dashboards.md for the
-- full rationale, including why this is a sibling table to `saved_views` rather than a
-- new `SavedViewPageType` value or a normalized `dashboard_panels` child table.
--
-- Same "config, not log data" CRUD-via-tombstone shape as `alert_rules` (migration 0003)
-- and `saved_views` (migration 0009) - a plain MergeTree has no in-place UPDATE/DELETE
-- ergonomics fit for "write, then immediately read back the new value." Instead:
--   * Every create/update INSERTs a brand-new row for the same `Id`, versioned by
--     `UpdatedAt`.
--   * Delete INSERTs another new version with `IsDeleted = 1` (a tombstone), never a real
--     row removal.
--   * All reads go through `FROM dashboards FINAL WHERE IsDeleted = 0`, which collapses
--     each `Id` down to its highest-`UpdatedAt` row per `ReplacingMergeTree(UpdatedAt)`'s
--     semantics.
-- Same `FINAL`-cost acceptance as `alert_rules`/`saved_views`: expected row count is tens
-- to low hundreds for a self-hosted instance, nothing like `logs`' volume.
CREATE TABLE IF NOT EXISTS clickhousedb.dashboards
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- The dashboard's panel list - an opaque JSON array of
    -- `{ id, panelType, title, layout: {x,y,w,h}, query }` objects, `query` itself being
    -- exactly the LogsFilterState / TracesFilterState / MetricsFilterState shape a
    -- `SavedView` of that page type already stores (src/dashboard/src/lib/{logs,traces,
    -- metrics}/state.svelte.ts). Stored opaque rather than exploded into columns or a
    -- child table for the same reason `saved_views.StateJson` is: this table never
    -- filters/aggregates/joins on a panel's contents, only round-trips the whole array -
    -- Flare.Api never deserializes it into a typed model, panel execution is entirely
    -- client-driven against the same endpoints the Explorer pages already call. See
    -- ADR-0023's "Alternatives considered" for why a child table and a typed
    -- AlertRule-style query union were both rejected for this pass.
    LayoutJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
