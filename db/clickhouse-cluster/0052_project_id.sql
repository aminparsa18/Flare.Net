-- Project ownership on config objects, migration 0052 - CLUSTER VARIANT (ADR-0123 phase 3).
--
-- Same column/rationale as db/clickhouse/0052_project_id.sql, applied to each table's `_local`
-- and Distributed pair.
ALTER TABLE clickhousedb.dashboards_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.dashboards ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.saved_views_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.saved_views ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.alert_rules_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.alert_rules ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.slos_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
ALTER TABLE clickhousedb.slos ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS ProjectId Nullable(UUID);
