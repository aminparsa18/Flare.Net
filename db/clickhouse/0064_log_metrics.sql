-- Log-based metrics schema, migration 0064.
--
-- Definitions of metrics derived from logs: a definition is a saved log condition (the same
-- `LogFilter` JSON `pipeline_rules.ConditionJson` holds) plus an optional list of attribute
-- keys to group by. `Flare.Ingest` counts the matching log events in every flush batch and
-- writes the counts to `metrics_sum` as delta sums named `MetricName`, so charts, alerts and
-- the metrics catalog treat them like any other metric (see
-- docs-internal/adr/0140-log-based-metrics.md). `Flare.Api` owns writes, `Flare.Ingest` polls
-- this table read-only.
--
-- Same "config, not telemetry" CRUD-via-tombstone shape as `pipeline_rules` (migration 0024):
-- every create/update INSERTs a new version of the `Id` keyed by `UpdatedAt`, delete INSERTs a
-- version with `IsDeleted = 1`, and reads take the latest version per `Id` (`LatestVersionSql`,
-- not `FINAL` - see ADR-0074).
CREATE TABLE IF NOT EXISTS clickhousedb.log_metrics
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    Enabled UInt8,
    IsDeleted UInt8 DEFAULT 0,

    -- The emitted metric's name, e.g. `logs.checkout.errors`.
    MetricName String CODEC(ZSTD(1)),

    -- Which logs are counted: a `LogFilter` as JSON, round-tripped through the C# model.
    ConditionJson String CODEC(ZSTD(1)),

    -- JSON array of attribute keys; each becomes a data-point attribute of the metric.
    GroupByJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
