-- Alerting schema, migration 0042.
--
-- User-defined alert rule labels and label-scoped maintenance windows - see
-- `docs-internal/adr/0084-alert-rule-labels.md`.
--
-- `alert_rules.LabelsJson`: the rule's user-set `{"team":"payments"}` key/value labels, as a
-- JSON object (same String-holding-JSON shape as `ConditionJson`/`MetricConditionJson`).
-- '{}' - the default, so every pre-existing rule - means no labels.
--
-- `maintenance_windows.LabelMatchersJson`: a JSON object of label key/value pairs; a rule is
-- covered when its labels contain every pair (in addition to any explicit `RuleIds`). '{}' (the
-- default) means "no label matcher" - the window then behaves exactly as before.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0041, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS LabelsJson String DEFAULT '{}' AFTER ThresholdUnit;

ALTER TABLE clickhousedb.maintenance_windows
    ADD COLUMN IF NOT EXISTS LabelMatchersJson String DEFAULT '{}' AFTER RuleIds;
