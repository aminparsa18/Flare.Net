-- Alerting schema, migration 0003.
--
-- Rule definitions for the Alerting roadmap item: a saved `Flare.Api.Model.LogFilter`
-- condition plus a count threshold over a rolling window, evaluated periodically by
-- `Flare.Api`'s `AlertEvaluationWorker`. See db/clickhouse/README.md's "Design
-- decisions" for the full rationale.
--
-- CRUD on a MergeTree-family table has no in-place UPDATE/DELETE ergonomics like a
-- typical OLTP store - `ALTER TABLE ... UPDATE/DELETE` are async *mutations*, the wrong
-- tool for "write, then immediately read back the new value" CRUD. Instead:
--   * Every create/update INSERTs a brand-new row for the same `Id`, versioned by
--     `UpdatedAt`.
--   * Delete INSERTs another new version with `IsDeleted = 1` (a tombstone), never a
--     real row removal.
--   * All reads go through `FROM alert_rules FINAL WHERE IsDeleted = 0`, which collapses
--     each `Id` down to its highest-`UpdatedAt` row per `ReplacingMergeTree(UpdatedAt)`'s
--     semantics.
-- `FINAL`'s per-part merge cost is real but negligible at this table's expected size (a
-- self-hosted instance: tens, maybe low hundreds of rules - nothing like `logs`' volume).
-- If that ever stops being true, the reviewed fallback is
-- `SELECT ... FROM alert_rules GROUP BY Id ORDER BY UpdatedAt DESC LIMIT 1 BY Id
-- HAVING IsDeleted = 0` (no `FINAL`, one extra `LIMIT 1 BY` pass) - not built now,
-- named here so it isn't rediscovered from scratch later.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_rules
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,

    -- Flare.Api.Model.LogFilter, JSON-serialized as-is (its own From/To are ignored -
    -- AlertEvaluationWorker supplies its own rolling window derived from WindowSeconds
    -- below at evaluation time). Stored opaque rather than exploded into columns: this
    -- table doesn't need to filter/aggregate on the condition's contents, only round-trip
    -- it through the C# model - re-deriving LogFilterSqlBuilder's shape as columns here
    -- would just be a second, harder-to-keep-in-sync copy of that shape.
    ConditionJson String CODEC(ZSTD(1)),

    ThresholdCount UInt64,
    -- "GreaterThanOrEqual" | "LessThan" - Flare.Api.Model.ThresholdComparator's string
    -- names verbatim (the camelCase wire form is applied by AlertsJsonContext at the API
    -- boundary, not here). LowCardinality: a closed, tiny enum.
    ThresholdComparator LowCardinality(String),
    WindowSeconds UInt32,
    CooldownSeconds UInt32,
    WebhookUrl String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
