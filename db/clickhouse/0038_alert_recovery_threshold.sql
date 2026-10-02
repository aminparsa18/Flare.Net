-- Alerting schema, migration 0038.
--
-- Recovery threshold (hysteresis) for alert rules - see
-- `docs-internal/adr/0076-alert-recovery-threshold.md`.
--
-- `alert_rules.RecoveryThreshold`: once a rule is firing, it resolves only after its observed
-- value (a count, or a metric value) has crossed this value back past the threshold, rather
-- than on the first evaluation that isn't breached. NULL (the column default, so every
-- pre-existing rule) disables it - the old behavior, unchanged.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0037, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS RecoveryThreshold Nullable(Float64) AFTER NotificationBodyTemplate;
