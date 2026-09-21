-- Pipeline rules schema, migration 0024.
--
-- Storage for the "User-defined field extraction/redaction at ingest" roadmap item -
-- Phase 1 (the rule engine; dry-run preview is a later phase). A rule is a
-- `Flare.Api.Model.LogFilter` scoping condition (the same shape `/api/logs/*` and
-- `alert_rules` already use) plus an ordered list of regex-based extraction/redaction
-- actions, evaluated by `Flare.Ingest` at flush time (see
-- `Flare.Ingest.Pipeline.Rules.PipelineRuleAnnotator`) - `Flare.Api` owns writes,
-- `Flare.Ingest` polls this table read-only. See
-- docs-internal/adr/0033-pipeline-rules-extraction-redaction.md for the full rationale.
--
-- Same "config, not log data" CRUD-via-tombstone shape as `alert_rules` (migration 0003)
-- and `dashboards` (migration 0020) - a plain MergeTree has no in-place UPDATE/DELETE
-- ergonomics fit for "write, then immediately read back the new value." Instead:
--   * Every create/update INSERTs a brand-new row for the same `Id`, versioned by
--     `UpdatedAt`.
--   * Delete INSERTs another new version with `IsDeleted = 1` (a tombstone), never a real
--     row removal.
--   * All reads go through `FROM pipeline_rules FINAL WHERE IsDeleted = 0`, which
--     collapses each `Id` down to its highest-`UpdatedAt` row per
--     `ReplacingMergeTree(UpdatedAt)`'s semantics.
-- Same `FINAL`-cost acceptance as `alert_rules`/`dashboards`: expected row count is tens
-- to low hundreds for a self-hosted instance, nothing like `logs`' volume - and
-- `Flare.Ingest` only re-reads it once per `PipelineRuleOptions.RefreshInterval`, not
-- per flush batch.
CREATE TABLE IF NOT EXISTS clickhousedb.pipeline_rules
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,

    -- Flare.Api.Model.LogFilter, JSON-serialized as-is (From/To/PatternId are ignored at
    -- match time - there's no time window at ingest, and PatternId doesn't exist yet
    -- when pipeline rules run, before Drain clustering). Stored opaque rather than
    -- exploded into columns, same reasoning as `alert_rules.ConditionJson`.
    ConditionJson String CODEC(ZSTD(1)),

    -- Ordered `Flare.Api.Model.PipelineRuleAction[]`, JSON-serialized as-is. Stored
    -- opaque for the same reason `ConditionJson` is - this table never filters/
    -- aggregates on an action's contents, only round-trips the whole array through the
    -- C# model on both the Flare.Api (write) and Flare.Ingest (read) sides.
    ActionsJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
