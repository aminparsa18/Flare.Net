-- Alerting schema, migration 0014 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0014_alert_rules_metric_condition.sql, applied
-- to both `alert_rules_local` and `alert_rules`.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ConditionKind LowCardinality(String) DEFAULT 'LogCount' AFTER IsDeleted,
    ADD COLUMN IF NOT EXISTS MetricConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER PagerDutyRoutingKey,
    ADD COLUMN IF NOT EXISTS MetricThresholdValue Nullable(Float64) AFTER MetricConditionJson;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ConditionKind LowCardinality(String) DEFAULT 'LogCount' AFTER IsDeleted,
    ADD COLUMN IF NOT EXISTS MetricConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER PagerDutyRoutingKey,
    ADD COLUMN IF NOT EXISTS MetricThresholdValue Nullable(Float64) AFTER MetricConditionJson;
