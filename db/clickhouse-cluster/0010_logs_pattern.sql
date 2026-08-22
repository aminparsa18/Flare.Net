-- Flare log storage schema, migration 0010 - CLUSTER VARIANT.
--
-- Same columns as db/clickhouse/0010_logs_pattern.sql, applied to both `logs_local` and
-- `logs` (see 0002_logs_event_id.sql's cluster variant for why both are needed).
ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS PatternId LowCardinality(String) DEFAULT '',
    ADD COLUMN IF NOT EXISTS PatternTemplate LowCardinality(String) DEFAULT '';
ALTER TABLE clickhousedb.logs ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS PatternId LowCardinality(String) DEFAULT '',
    ADD COLUMN IF NOT EXISTS PatternTemplate LowCardinality(String) DEFAULT '';
