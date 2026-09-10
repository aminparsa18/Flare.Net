-- Alerting schema, migration 0012 - CLUSTER VARIANT.
--
-- Same column as db/clickhouse/0012_alert_rules_pagerduty.sql, applied to both
-- `alert_rules_local` and `alert_rules`.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS PagerDutyRoutingKey String DEFAULT '' CODEC(ZSTD(1)) AFTER EmailTo;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS PagerDutyRoutingKey String DEFAULT '' CODEC(ZSTD(1)) AFTER EmailTo;
