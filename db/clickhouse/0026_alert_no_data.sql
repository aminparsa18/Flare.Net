-- Alerting schema, migration 0026.
--
-- Absent-data ("no data") alerting - see `docs-internal/adr/0045-absent-data-alerting.md`.
--
-- `alert_rules.NoDataWindowSeconds`: opt-in per-rule window. When > 0, the rule also fires
-- if its condition (a `LogCount` rule's log filter, or a `MetricThreshold` rule's metric
-- query) matched no data at all over the last this-many seconds - a dead exporter or a
-- service that stopped emitting. 0 (the column default, so every pre-existing rule) keeps
-- the old behavior unchanged. A plain column on the existing rule rather than a fourth
-- `ConditionKind` value, since it composes with a rule's threshold instead of replacing it.
--
-- `alert_events.NoData`: marks an event fired by that check rather than a threshold breach
-- (`ObservedCount` 0, `ObservedValue` NULL, `WindowSeconds` = the no-data window). Default 0
-- keeps every pre-existing event reading back as a threshold breach, which it was.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0025, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS NoDataWindowSeconds UInt32 DEFAULT 0 AFTER ExceptionConditionJson;
ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS NoData UInt8 DEFAULT 0;
