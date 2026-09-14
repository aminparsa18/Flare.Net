-- Dashboards schema, migration 0021 - CLUSTER VARIANT.
--
-- Same column as db/clickhouse/0021_dashboards_owner.sql, applied to both
-- `dashboards_local` and `dashboards`.
ALTER TABLE clickhousedb.dashboards_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS OwnerUserId Nullable(UUID) DEFAULT NULL AFTER UpdatedAt;
ALTER TABLE clickhousedb.dashboards ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS OwnerUserId Nullable(UUID) DEFAULT NULL AFTER UpdatedAt;
