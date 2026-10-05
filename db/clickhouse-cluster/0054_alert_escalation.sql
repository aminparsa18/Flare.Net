-- Alerting schema, migration 0054 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0054_alert_escalation.sql, applied to both the
-- `_local` tables and their Distributed counterparts.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EscalateAfterMinutes UInt32 DEFAULT 0,
    ADD COLUMN IF NOT EXISTS EscalationChannelIds Array(UUID) DEFAULT [];
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS EscalateAfterMinutes UInt32 DEFAULT 0,
    ADD COLUMN IF NOT EXISTS EscalationChannelIds Array(UUID) DEFAULT [];

ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Escalated UInt8 DEFAULT 0;
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Escalated UInt8 DEFAULT 0;
