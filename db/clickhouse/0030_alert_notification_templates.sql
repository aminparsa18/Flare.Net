-- Alerting schema, migration 0030.
--
-- Custom alert notification templates - see
-- `docs-internal/adr/0052-alert-notification-templates.md`.
--
-- `alert_rules.NotificationTitleTemplate`/`NotificationBodyTemplate`: optional per-rule
-- `{{placeholder}}` text that replaces the built-in notification title/body. '' (the column
-- default, so every pre-existing rule) keeps the built-in wording - the old behavior, unchanged.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0029, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS NotificationTitleTemplate String DEFAULT '' AFTER MinDataPoints,
    ADD COLUMN IF NOT EXISTS NotificationBodyTemplate String DEFAULT '' AFTER NotificationTitleTemplate;
