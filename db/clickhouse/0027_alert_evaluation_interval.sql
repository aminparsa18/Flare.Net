-- Alerting schema, migration 0027.
--
-- Per-rule evaluation frequency - see `docs-internal/adr/0046-per-rule-alert-evaluation-interval.md`.
--
-- `alert_rules.EvaluationIntervalSeconds`: how often `Flare.AlertWorker` re-evaluates this
-- rule. 0 (the column default, so every pre-existing rule) means "every poll tick"
-- (`Alerting:PollInterval`, 30s by default) - the old behavior, unchanged. A larger value
-- lets a slow/expensive rule (e.g. a wide-window metric query) run every 5m/15m instead.
-- The per-rule "last evaluated" timestamp the worker compares against lives in Redis, not
-- here - `alert_rules` is a ReplacingMergeTree, so writing it on every evaluation would
-- churn a new row version per rule per tick.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0026, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS EvaluationIntervalSeconds UInt32 DEFAULT 0 AFTER NoDataWindowSeconds;
