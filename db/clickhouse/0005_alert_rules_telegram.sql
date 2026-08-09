-- Alerting schema, migration 0005.
--
-- Adds a second, mutually-exclusive notification channel to `alert_rules` (migration
-- 0003): Telegram, sent via `Flare.Api.Alerting.TelegramAlertNotifier` instead of
-- `WebhookAlertNotifier` when these are set. `Flare.Api.Endpoints.AlertEndpoints`'s
-- channel validation rejects a rule with both `WebhookUrl` and Telegram fields set, or
-- neither - see that file's `ValidateChannel`.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file rather than editing 0003_alert_rules.sql.
-- Like migrations 0002-0004, there's no automated apply path yet beyond the local-dev
-- init-mount that only runs 0001 automatically - run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS TelegramBotToken String DEFAULT '' CODEC(ZSTD(1)) AFTER WebhookUrl,
    ADD COLUMN IF NOT EXISTS TelegramChatId String DEFAULT '' CODEC(ZSTD(1)) AFTER TelegramBotToken;
