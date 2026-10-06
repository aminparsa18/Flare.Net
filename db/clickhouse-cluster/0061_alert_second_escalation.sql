-- Alerting schema, migration 0061 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0061_alert_second_escalation.sql, applied to both the
-- `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SecondEscalateAfterMinutes UInt32 DEFAULT 0,
    ADD COLUMN IF NOT EXISTS SecondEscalationChannelIds Array(UUID) DEFAULT [];
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SecondEscalateAfterMinutes UInt32 DEFAULT 0,
    ADD COLUMN IF NOT EXISTS SecondEscalationChannelIds Array(UUID) DEFAULT [];
