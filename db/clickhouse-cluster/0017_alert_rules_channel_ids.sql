-- Alerting schema, migration 0017 - CLUSTER VARIANT.
--
-- Same column as db/clickhouse/0017_alert_rules_channel_ids.sql, applied to both
-- `alert_rules_local` and `alert_rules`.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ChannelIds Array(UUID) DEFAULT [] AFTER MetricThresholdValue;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ChannelIds Array(UUID) DEFAULT [] AFTER MetricThresholdValue;
