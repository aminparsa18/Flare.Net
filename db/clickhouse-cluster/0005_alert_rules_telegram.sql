-- Alerting schema, migration 0005 - CLUSTER VARIANT.
--
-- Same columns as db/clickhouse/0005_alert_rules_telegram.sql, applied to both
-- `alert_rules_local` and `alert_rules` (see 0002_logs_event_id.sql's cluster variant
-- for why both are needed).
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS TelegramBotToken String DEFAULT '' CODEC(ZSTD(1)) AFTER WebhookUrl,
    ADD COLUMN IF NOT EXISTS TelegramChatId String DEFAULT '' CODEC(ZSTD(1)) AFTER TelegramBotToken;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS TelegramBotToken String DEFAULT '' CODEC(ZSTD(1)) AFTER WebhookUrl,
    ADD COLUMN IF NOT EXISTS TelegramChatId String DEFAULT '' CODEC(ZSTD(1)) AFTER TelegramBotToken;
