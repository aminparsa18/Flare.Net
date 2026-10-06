-- Alerting schema, migration 0063 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0063_oncall_rotation_coverage.sql, added to both the
-- `_local` table and its Distributed counterpart.
ALTER TABLE clickhousedb.oncall_rotations_local ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Coverage String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.oncall_rotations ON CLUSTER 'flare_cluster'
    ADD COLUMN IF NOT EXISTS Coverage String DEFAULT '' CODEC(ZSTD(1));
