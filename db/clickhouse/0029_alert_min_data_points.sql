-- Alerting schema, migration 0029.
--
-- Minimum data points for metric alert evaluation - see
-- `docs-internal/adr/0050-alert-minimum-data-points.md`.
--
-- `alert_rules.MinDataPoints`: for a `MetricThreshold` rule, the fewest raw data points the
-- condition must match over the rule's window before the threshold is compared at all -
-- fewer is "insufficient data" and the rule doesn't fire. 0 (the column default, so every
-- pre-existing rule) disables the check - the old behavior, unchanged.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0028, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS MinDataPoints UInt32 DEFAULT 0 AFTER AnomalyConditionJson;
