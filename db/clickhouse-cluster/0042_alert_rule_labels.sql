-- Alerting schema, migration 0042 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0042_alert_rule_labels.sql, applied to both the
-- `_local` tables and their Distributed counterparts.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS LabelsJson String DEFAULT '{}' AFTER ThresholdUnit;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS LabelsJson String DEFAULT '{}' AFTER ThresholdUnit;

ALTER TABLE clickhousedb.maintenance_windows_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS LabelMatchersJson String DEFAULT '{}' AFTER RuleIds;
ALTER TABLE clickhousedb.maintenance_windows ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS LabelMatchersJson String DEFAULT '{}' AFTER RuleIds;
