-- Cluster-mode variant of db/clickhouse/0033_alert_resolved_notifications.sql - see that
-- file's comment for what these columns mean. Each column is added to the `_local`
-- replicated table and to its `Distributed` wrapper, same as every other ALTER in this
-- directory.
ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Resolved UInt8 DEFAULT 0;
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Resolved UInt8 DEFAULT 0;

ALTER TABLE clickhousedb.notification_channels_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SendResolved UInt8 DEFAULT 1 AFTER PagerDutyRoutingKey;
ALTER TABLE clickhousedb.notification_channels ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS SendResolved UInt8 DEFAULT 1 AFTER PagerDutyRoutingKey;
