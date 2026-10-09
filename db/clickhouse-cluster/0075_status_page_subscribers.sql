-- Status pages, migration 0075 - CLUSTER VARIANT. Same column/rationale as
-- db/clickhouse/0075_status_page_subscribers.sql, added to the replicated storage table and the
-- Distributed one (same pair as migration 0068).
ALTER TABLE clickhousedb.status_pages_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS SubscriberChannelIds Array(UUID) DEFAULT [];

ALTER TABLE clickhousedb.status_pages ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS SubscriberChannelIds Array(UUID) DEFAULT [];
