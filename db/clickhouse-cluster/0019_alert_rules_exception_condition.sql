-- Alerting schema, migration 0019 - CLUSTER VARIANT.
--
-- Same column as db/clickhouse/0019_alert_rules_exception_condition.sql, applied to both
-- `alert_rules_local` and `alert_rules`.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ExceptionConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER ChannelIds;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ExceptionConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER ChannelIds;
