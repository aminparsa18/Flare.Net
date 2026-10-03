-- Alerting schema, migration 0046.
--
-- JSM Ops notification channel - see `docs-internal/adr/0099-jsm-ops-notification-channel.md`.
-- `notification_channels.JsmOpsApiKey`: the JSM Operations integration API key. '' (the default,
-- so every pre-existing channel) means unset.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0045, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.notification_channels
    ADD COLUMN IF NOT EXISTS JsmOpsApiKey String DEFAULT '' AFTER IncidentIoToken;
