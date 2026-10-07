-- Retention operations, migration 0067.
--
-- One row-version per (TransactionId, Signal): `Flare.Api`'s `PUT /api/retention` inserts a
-- `pending` row for every signal it was asked to change, and its background worker inserts a
-- newer version of the same key (`success` / `failed`) once the `ALTER TABLE ... MODIFY TTL`
-- finished - the same insert-a-new-version-keyed-by-`UpdatedAt` shape as the config tables
-- (`pipeline_rules`, `log_metrics`), read with `ORDER BY UpdatedAt DESC LIMIT 1 BY ...` so a
-- cluster's `rand()` sharding can't surface a stale version. See
-- docs-internal/adr/0143-retention-ttl.md.
--
-- `Days = 0` means "keep forever" (the table's TTL is removed). The latest non-failed row per
-- `Signal` is the *expected* retention; the *actual* one is parsed live from the data tables'
-- `create_table_query`, so a hand-edited TTL shows up as drift instead of being hidden.
--
-- The history itself expires after a year - it is an audit trail of settings changes, not data.
CREATE TABLE IF NOT EXISTS clickhousedb.retention_operations
(
    TransactionId UUID,
    Signal LowCardinality(String),
    Days UInt32,
    Status LowCardinality(String),
    Error String CODEC(ZSTD(1)),
    RequestedBy String CODEC(ZSTD(1)),
    RequestedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (TransactionId, Signal)
TTL toDateTime(RequestedAt) + INTERVAL 365 DAY
SETTINGS index_granularity = 8192;
