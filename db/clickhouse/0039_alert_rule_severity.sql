-- Alerting schema, migration 0039.
--
-- Rule-level severity - see `docs-internal/adr/0077-alert-rule-severity.md`.
--
-- `alert_rules.Severity`: Critical / Error / Warning / Info, sent as PagerDuty's `severity`,
-- shown in the other channels' messages, and exposed as the `{{severity}}` template
-- placeholder. Defaults to 'Critical' so every pre-existing rule keeps paging as it always did.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0038, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS Severity LowCardinality(String) DEFAULT 'Critical' AFTER RecoveryThreshold;
