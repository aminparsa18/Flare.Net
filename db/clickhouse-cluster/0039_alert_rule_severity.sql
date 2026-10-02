-- Alerting schema, migration 0039 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0039_alert_rule_severity.sql, applied to both
-- the `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Severity LowCardinality(String) DEFAULT 'Critical' AFTER RecoveryThreshold;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Severity LowCardinality(String) DEFAULT 'Critical' AFTER RecoveryThreshold;
