-- Dashboards schema, migration 0043 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0043_dashboard_tags.sql, applied to both
-- `dashboards_local` and `dashboards`.
ALTER TABLE clickhousedb.dashboards_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Tags Array(String) DEFAULT [] AFTER Description;
ALTER TABLE clickhousedb.dashboards ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Tags Array(String) DEFAULT [] AFTER Description;
