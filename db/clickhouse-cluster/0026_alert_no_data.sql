-- Alerting schema, migration 0026 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0026_alert_no_data.sql, applied to both the
-- `_local` tables and their Distributed counterparts.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NoDataWindowSeconds UInt32 DEFAULT 0 AFTER ExceptionConditionJson;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NoDataWindowSeconds UInt32 DEFAULT 0 AFTER ExceptionConditionJson;
ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NoData UInt8 DEFAULT 0;
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NoData UInt8 DEFAULT 0;
