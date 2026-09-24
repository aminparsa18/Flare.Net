-- Alerting schema, migration 0028 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0028_alert_anomaly.sql, applied to both the
-- `_local` tables and their Distributed counterparts.
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS AnomalyConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER EvaluationIntervalSeconds;
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS AnomalyConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER EvaluationIntervalSeconds;
ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS BaselineMean Nullable(Float64);
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS BaselineMean Nullable(Float64);
ALTER TABLE clickhousedb.alert_events_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ZScore Nullable(Float64);
ALTER TABLE clickhousedb.alert_events ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ZScore Nullable(Float64);
