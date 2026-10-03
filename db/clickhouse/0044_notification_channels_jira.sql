-- Alerting schema, migration 0044.
--
-- Jira notification channel - see `docs-internal/adr/0097-jira-notification-channel.md`.
-- Five destination fields used only by `notification_channels.Type = 'Jira'` rows; '' (the
-- default, so every pre-existing channel) means unset.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0043, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.notification_channels
    ADD COLUMN IF NOT EXISTS JiraBaseUrl String DEFAULT '' AFTER SendResolved,
    ADD COLUMN IF NOT EXISTS JiraEmail String DEFAULT '' AFTER JiraBaseUrl,
    ADD COLUMN IF NOT EXISTS JiraApiToken String DEFAULT '' AFTER JiraEmail,
    ADD COLUMN IF NOT EXISTS JiraProjectKey String DEFAULT '' AFTER JiraApiToken,
    ADD COLUMN IF NOT EXISTS JiraIssueType String DEFAULT '' AFTER JiraProjectKey;
