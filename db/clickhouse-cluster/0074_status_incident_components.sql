-- Status page incidents, migration 0074 - CLUSTER VARIANT. Same column/rationale as
-- db/clickhouse/0074_status_incident_components.sql, added to the replicated storage table and the
-- Distributed one (same pair as migration 0068).
ALTER TABLE clickhousedb.status_incidents_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Components Array(UUID) DEFAULT [];

ALTER TABLE clickhousedb.status_incidents ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Components Array(UUID) DEFAULT [];
