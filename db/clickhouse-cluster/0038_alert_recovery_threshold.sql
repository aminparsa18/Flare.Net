-- Alerting schema, migration 0038 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0038_alert_recovery_threshold.sql, applied to both
-- the `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS RecoveryThreshold Nullable(Float64) AFTER NotificationBodyTemplate;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS RecoveryThreshold Nullable(Float64) AFTER NotificationBodyTemplate;
