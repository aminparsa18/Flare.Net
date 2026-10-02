-- Alerting schema, migration 0040 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0040_alert_threshold_unit.sql, applied to both
-- the `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ThresholdUnit String DEFAULT '' AFTER Severity;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ThresholdUnit String DEFAULT '' AFTER Severity;
