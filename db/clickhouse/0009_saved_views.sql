-- Saved views schema, migration 0009.
--
-- Storage for the "Saved dashboards / shareable views" roadmap item: a named, reloadable
-- snapshot of one dashboard page's (Logs / Traces / Metrics) filter/selection state, CRUD'd
-- via Flare.Api's `/api/views` endpoints. See db/clickhouse/README.md's "Design decisions"
-- for the full rationale.
--
-- Second "config, not log data" table after `alert_rules` (migration 0003) - same CRUD
-- problem, same fix: a plain MergeTree has no in-place UPDATE/DELETE ergonomics fit for
-- "write, then immediately read back the new value." Instead:
--   * Every create/update INSERTs a brand-new row for the same `Id`, versioned by
--     `UpdatedAt`.
--   * Delete INSERTs another new version with `IsDeleted = 1` (a tombstone), never a real
--     row removal.
--   * All reads go through `FROM saved_views FINAL WHERE IsDeleted = 0`, which collapses
--     each `Id` down to its highest-`UpdatedAt` row per `ReplacingMergeTree(UpdatedAt)`'s
--     semantics.
-- Same `FINAL`-cost acceptance as `alert_rules`: expected row count is tens to low
-- hundreds for a self-hosted instance, nothing like `logs`' volume. Same reviewed fallback
-- if that ever stops holding: `GROUP BY Id ORDER BY UpdatedAt DESC LIMIT 1 BY Id HAVING
-- IsDeleted = 0` instead of `FINAL`.
CREATE TABLE IF NOT EXISTS clickhousedb.saved_views
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- "Logs" | "Traces" | "Metrics" - Flare.Api.Model.SavedViewPageType's string names
    -- verbatim (the camelCase wire form is applied by SavedViewsJsonContext at the API
    -- boundary, not here). LowCardinality: a closed, tiny enum, same convention as
    -- alert_rules.ThresholdComparator.
    PageType LowCardinality(String),

    -- The dashboard's own per-page filter/selection shape (LogsFilterState /
    -- TracesFilterState / a Metrics equivalent, all TypeScript-owned - see
    -- src/dashboard/src/lib/{logs,traces,metrics}/state.svelte.ts), JSON-serialized
    -- opaque. Stored opaque rather than exploded into columns for the same reason
    -- alert_rules.ConditionJson is: this table never filters/aggregates on the state's
    -- contents, only round-trips it - one level more opaque than ConditionJson, since that
    -- column at least deserializes into a real C# LogFilter, while this one is never
    -- interpreted by Flare.Api at all (round-tripped as JsonElement).
    StateJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
