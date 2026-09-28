-- Index-backed Logs free-text search, migration 0036 - CLUSTER VARIANT.
--
-- Same index/rationale as db/clickhouse/0036_logs_body_ngram_index.sql - see that file
-- for the full explanation. Only `logs_local` gets it: a skip index is part of a
-- MergeTree table's storage, and the `logs` Distributed table has none of its own - it
-- forwards each query to every shard's `logs_local`, where the index is used.
ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster' ADD INDEX IF NOT EXISTS idx_body_ngram lowerUTF8(Body) TYPE ngrambf_v1(4, 32768, 3, 0) GRANULARITY 1;
