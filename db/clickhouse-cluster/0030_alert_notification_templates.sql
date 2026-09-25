-- Alerting schema, migration 0030 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0030_alert_notification_templates.sql, applied to
-- both the `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NotificationTitleTemplate String DEFAULT '' AFTER MinDataPoints,
    ADD COLUMN IF NOT EXISTS NotificationBodyTemplate String DEFAULT '' AFTER NotificationTitleTemplate;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS NotificationTitleTemplate String DEFAULT '' AFTER MinDataPoints,
    ADD COLUMN IF NOT EXISTS NotificationBodyTemplate String DEFAULT '' AFTER NotificationTitleTemplate;
