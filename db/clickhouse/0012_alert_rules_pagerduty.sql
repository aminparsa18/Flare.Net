-- Alerting schema, migration 0012.
--
-- Adds a fourth, mutually-exclusive notification channel to `alert_rules` (migration
-- 0003, extended by migration 0005's Telegram columns and migration 0006's Email
-- column): PagerDuty, sent via `Flare.Api.Alerting.PagerDutyAlertNotifier` against
-- PagerDuty's fixed Events API v2 endpoint - unlike Email, there's no app-wide server
-- config to go with it, since the routing key alone is enough to address PagerDuty's
-- endpoint. `Flare.Api.Model.AlertRuleRequest.ValidateChannel` rejects a rule with more
-- than one of `WebhookUrl`, the Telegram fields, `EmailTo`, or `PagerDutyRoutingKey` set,
-- or none of them.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file rather than editing 0003_alert_rules.sql.
-- Like migrations 0002-0011, there's no automated apply path yet beyond the local-dev
-- init-mount that only runs 0001 automatically - run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS PagerDutyRoutingKey String DEFAULT '' CODEC(ZSTD(1)) AFTER EmailTo;
