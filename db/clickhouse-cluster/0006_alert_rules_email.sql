-- Alerting schema, migration 0006 - CLUSTER VARIANT.
--
-- Same column as db/clickhouse/0006_alert_rules_email.sql, applied to both
-- `alert_rules_local` and `alert_rules`.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EmailTo String DEFAULT '' CODEC(ZSTD(1)) AFTER TelegramChatId;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EmailTo String DEFAULT '' CODEC(ZSTD(1)) AFTER TelegramChatId;
