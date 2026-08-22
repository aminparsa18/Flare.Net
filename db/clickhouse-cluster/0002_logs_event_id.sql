-- Flare log storage schema, migration 0002 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0002_logs_event_id.sql. A `Distributed` table's
-- column set is fixed at creation time and does not auto-sync from its underlying local
-- table, so both `logs_local` (the real storage) and `logs` (the Distributed wrapper,
-- migration 0001) need the same ADD COLUMN.
ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS EventId UUID;
ALTER TABLE clickhousedb.logs ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS EventId UUID;
