-- Cluster-mode variant of db/clickhouse/0046_notification_channels_jsmops.sql - see that file's
-- comment. Added to the `_local` replicated table and its `Distributed` wrapper.
ALTER TABLE clickhousedb.notification_channels_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS JsmOpsApiKey String DEFAULT '' AFTER IncidentIoToken;
ALTER TABLE clickhousedb.notification_channels ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS JsmOpsApiKey String DEFAULT '' AFTER IncidentIoToken;
