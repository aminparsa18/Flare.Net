-- Alerting schema, migration 0018 - CLUSTER VARIANT.
--
-- Same column as db/clickhouse/0018_alert_events_channel_results.sql, applied to both
-- `alert_events_local` and `alert_events`, same as migration 0015's cluster variant did.
ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ChannelResultsJson String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ChannelResultsJson String DEFAULT '' CODEC(ZSTD(1));
