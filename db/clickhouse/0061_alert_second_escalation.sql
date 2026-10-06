-- Alerting schema, migration 0061.
--
-- Second escalation step - see `docs-internal/adr/0136-alert-multi-step-escalation.md`.
--
-- `alert_rules.SecondEscalateAfterMinutes`: 0 (the default, and every existing rule) means the
-- rule has one escalation step. Otherwise an incident still unacknowledged this many minutes after
-- its first escalation is sent once more, to `alert_rules.SecondEscalationChannelIds`.
--
-- No `alert_events` change: the existing `Escalated` UInt8 now holds the step number (1 or 2), so
-- every row written before this migration (Escalated = 1) already reads as a first step.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0060, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS SecondEscalateAfterMinutes UInt32 DEFAULT 0,
    ADD COLUMN IF NOT EXISTS SecondEscalationChannelIds Array(UUID) DEFAULT [];
