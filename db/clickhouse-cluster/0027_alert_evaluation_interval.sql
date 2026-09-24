-- Alerting schema, migration 0027 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0027_alert_evaluation_interval.sql, applied to both
-- the `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EvaluationIntervalSeconds UInt32 DEFAULT 0 AFTER NoDataWindowSeconds;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EvaluationIntervalSeconds UInt32 DEFAULT 0 AFTER NoDataWindowSeconds;
