-- Alerting schema, migration 0033.
--
-- "Resolved" alert notifications - see
-- `docs-internal/adr/0064-alert-resolved-notifications.md`.
--
-- `alert_events.Resolved`: 1 marks a resolution event - the rule's condition recovered after
-- it had fired - rather than a fire. A rule is firing while its latest fire (`Resolved = 0`)
-- is newer than its latest resolution; that per-rule firing/ok state is derived from this
-- table, not stored separately. 0 (the column default, so every pre-existing row) is a fire.
-- A resolution row's `FiredAt` is when the recovery was observed, and its
-- `NotificationStatus` is 'Sent'/'Failed', or 'Skipped' when nothing needed sending.
--
-- `notification_channels.SendResolved`: per-channel opt-out of resolution notifications.
-- 1 (the column default, so every pre-existing channel) sends them.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0032, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS Resolved UInt8 DEFAULT 0;

ALTER TABLE clickhousedb.notification_channels
    ADD COLUMN IF NOT EXISTS SendResolved UInt8 DEFAULT 1 AFTER PagerDutyRoutingKey;
