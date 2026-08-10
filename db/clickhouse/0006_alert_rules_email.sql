-- Alerting schema, migration 0006.
--
-- Adds a third, mutually-exclusive notification channel to `alert_rules` (migration
-- 0003, extended by migration 0005's Telegram columns): Email, sent via
-- `Flare.Api.Alerting.EmailAlertNotifier` through the app-wide SMTP server configured by
-- `Flare.Api.Alerting.EmailOptions` (not stored per-rule - only the recipient is).
-- `Flare.Api.Model.AlertRuleRequest.ValidateChannel` rejects a rule with more than one of
-- `WebhookUrl`, the Telegram fields, or `EmailTo` set, or none of them.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file rather than editing 0003_alert_rules.sql.
-- Like migrations 0002-0005, there's no automated apply path yet beyond the local-dev
-- init-mount that only runs 0001 automatically - run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS EmailTo String DEFAULT '' CODEC(ZSTD(1)) AFTER TelegramChatId;
