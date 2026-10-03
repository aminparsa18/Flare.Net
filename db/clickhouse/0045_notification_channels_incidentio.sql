-- Alerting schema, migration 0045.
--
-- incident.io notification channel - see `docs-internal/adr/0098-incidentio-notification-channel.md`.
-- `notification_channels.IncidentIoToken`: bearer token of the incident.io HTTP alert source
-- (the source URL goes in the existing `WebhookUrl`). '' (the default, so every pre-existing
-- channel) means unset.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0044, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.notification_channels
    ADD COLUMN IF NOT EXISTS IncidentIoToken String DEFAULT '' AFTER JiraIssueType;
