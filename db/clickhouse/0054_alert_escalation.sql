-- Alerting schema, migration 0054.
--
-- Alert escalation - see `docs-internal/adr/0125-alert-escalation.md`.
--
-- `alert_rules.EscalateAfterMinutes`: 0 (the default, and every existing rule) disables
-- escalation. Otherwise, a firing incident nobody has acknowledged this many minutes after it
-- first notified is escalated once, to `alert_rules.EscalationChannelIds`.
--
-- `alert_events.Escalated`: 1 marks the row the escalation itself records. It is how the worker
-- knows the current incident already escalated, so there is no separate state table (same
-- reasoning as ADR-0064). Escalation rows are ignored when working out the last fire and the
-- cooldown.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0053, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS EscalateAfterMinutes UInt32 DEFAULT 0,
    ADD COLUMN IF NOT EXISTS EscalationChannelIds Array(UUID) DEFAULT [];

ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS Escalated UInt8 DEFAULT 0;
