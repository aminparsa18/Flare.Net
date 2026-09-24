-- Alerting schema, migration 0029 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0029_alert_min_data_points.sql, applied to both
-- the `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS MinDataPoints UInt32 DEFAULT 0 AFTER AnomalyConditionJson;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS MinDataPoints UInt32 DEFAULT 0 AFTER AnomalyConditionJson;
