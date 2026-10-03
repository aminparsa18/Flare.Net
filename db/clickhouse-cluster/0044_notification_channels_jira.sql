-- Cluster-mode variant of db/clickhouse/0044_notification_channels_jira.sql - see that file's
-- comment. Added to the `_local` replicated table and its `Distributed` wrapper, as in 0033.
ALTER TABLE clickhousedb.notification_channels_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS JiraBaseUrl String DEFAULT '' AFTER SendResolved,
    ADD COLUMN IF NOT EXISTS JiraEmail String DEFAULT '' AFTER JiraBaseUrl,
    ADD COLUMN IF NOT EXISTS JiraApiToken String DEFAULT '' AFTER JiraEmail,
    ADD COLUMN IF NOT EXISTS JiraProjectKey String DEFAULT '' AFTER JiraApiToken,
    ADD COLUMN IF NOT EXISTS JiraIssueType String DEFAULT '' AFTER JiraProjectKey;
ALTER TABLE clickhousedb.notification_channels ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS JiraBaseUrl String DEFAULT '' AFTER SendResolved,
    ADD COLUMN IF NOT EXISTS JiraEmail String DEFAULT '' AFTER JiraBaseUrl,
    ADD COLUMN IF NOT EXISTS JiraApiToken String DEFAULT '' AFTER JiraEmail,
    ADD COLUMN IF NOT EXISTS JiraProjectKey String DEFAULT '' AFTER JiraApiToken,
    ADD COLUMN IF NOT EXISTS JiraIssueType String DEFAULT '' AFTER JiraProjectKey;
