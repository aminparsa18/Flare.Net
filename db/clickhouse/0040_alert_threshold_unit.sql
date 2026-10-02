-- Alerting schema, migration 0040.
--
-- Threshold unit for metric-threshold alert rules - see
-- `docs-internal/adr/0080-alert-threshold-unit.md`.
--
-- `alert_rules.ThresholdUnit`: the unit the rule's `MetricThresholdValue` (and
-- `RecoveryThreshold`) was typed in, e.g. 'ms' for a rule against a metric recorded in
-- seconds. The alert worker converts it to the series' own unit before comparing. Empty (the
-- default, so every pre-existing rule) means "already in the series' unit" - the old behavior.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0039, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS ThresholdUnit String DEFAULT '' AFTER Severity;
