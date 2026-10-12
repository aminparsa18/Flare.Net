-- Status page subscriber component filter, migration 0077 - CLUSTER VARIANT. Same column/rationale as
-- db/clickhouse/0077_status_subscriber_components.sql, added to the replicated storage table and the
-- Distributed one (same pair as migration 0075).
ALTER TABLE clickhousedb.status_subscribers_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Components Array(UUID) DEFAULT [];

ALTER TABLE clickhousedb.status_subscribers ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Components Array(UUID) DEFAULT [];
