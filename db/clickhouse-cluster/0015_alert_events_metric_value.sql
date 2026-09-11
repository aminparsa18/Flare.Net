-- Alerting schema, migration 0015 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0015_alert_events_metric_value.sql, applied to
-- both `alert_events_local` and `alert_events`.
ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ConditionKind LowCardinality(String) DEFAULT 'LogCount' AFTER RuleName,
    ADD COLUMN IF NOT EXISTS ObservedValue Nullable(Float64) AFTER ThresholdCount,
    ADD COLUMN IF NOT EXISTS ThresholdValue Nullable(Float64) AFTER ObservedValue;
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ConditionKind LowCardinality(String) DEFAULT 'LogCount' AFTER RuleName,
    ADD COLUMN IF NOT EXISTS ObservedValue Nullable(Float64) AFTER ThresholdCount,
    ADD COLUMN IF NOT EXISTS ThresholdValue Nullable(Float64) AFTER ObservedValue;
