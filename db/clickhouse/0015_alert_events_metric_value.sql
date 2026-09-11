-- Alerting schema, migration 0015.
--
-- Adds the metric-condition equivalents of `alert_events`' (migration 0004) existing
-- `ObservedCount`/`ThresholdCount` columns, for events fired by a `ConditionKind =
-- 'MetricThreshold'` rule (migration 0014). Snapshotted onto the event row, same
-- reasoning `RuleName`/`ThresholdCount`/`WindowSeconds` are already snapshotted rather
-- than looked up live from the (possibly since-edited or -deleted) rule.
--
-- `Nullable(Float64)`, not `Float64 DEFAULT 0` - same "0.0 is a plausible real value"
-- reasoning migration 0014's `MetricThresholdValue` comment gives; a real `NULL` here
-- means "this event came from a log-count rule, see `ObservedCount`/`ThresholdCount`
-- instead" rather than colliding with a metric value that happens to be zero.
-- `ConditionKind` mirrors migration 0014's column of the same name/default, so a history
-- row is self-describing without joining back to `alert_rules FINAL`.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file rather than editing 0004_alert_events.sql.
-- Like migrations 0002-0014, there's no automated apply path yet beyond the local-dev
-- init-mount that only runs 0001 automatically - run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS ConditionKind LowCardinality(String) DEFAULT 'LogCount' AFTER RuleName,
    ADD COLUMN IF NOT EXISTS ObservedValue Nullable(Float64) AFTER ThresholdCount,
    ADD COLUMN IF NOT EXISTS ThresholdValue Nullable(Float64) AFTER ObservedValue;
