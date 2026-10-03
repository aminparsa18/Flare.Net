-- Cluster-mode variant of db/clickhouse/0045_notification_channels_incidentio.sql - see that
-- file's comment. Added to the `_local` replicated table and its `Distributed` wrapper.
ALTER TABLE clickhousedb.notification_channels_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS IncidentIoToken String DEFAULT '' AFTER JiraIssueType;
ALTER TABLE clickhousedb.notification_channels ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS IncidentIoToken String DEFAULT '' AFTER JiraIssueType;
