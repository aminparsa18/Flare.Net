-- Data-subject erasure operations, migration 0084 (docs-internal/adr/0198-data-subject-erasure-export.md).
--
-- One row-version per erasure request: `Flare.Api`'s `POST /api/erasure` inserts a `pending` row, its
-- background worker inserts a newer version (`submitted` once every table's `ALTER TABLE ... DELETE`
-- mutation is queued, or `failed`). Same insert-a-new-version-keyed-by-`UpdatedAt` shape as
-- `retention_operations` (0067), read with `ORDER BY UpdatedAt DESC LIMIT 1 BY TransactionId`.
-- Whether a queued mutation has finished is not stored: it is read live from `system.mutations`.
--
-- The row never holds the identifier values themselves - they are the personal data being erased.
-- `IdentifierKeys` names the attributes that were matched (`user.id`, `user.email`, ...) and `SubjectHash`
-- is a short SHA-256 prefix of the values so two requests for the same subject can be correlated.
--
-- Kept for three years: this is the evidence that an erasure request was honoured, not telemetry.
CREATE TABLE IF NOT EXISTS clickhousedb.erasure_operations
(
    TransactionId UUID,
    Status LowCardinality(String),
    Error String CODEC(ZSTD(1)),
    IdentifierKeys Array(String) CODEC(ZSTD(1)),
    SubjectHash String CODEC(ZSTD(1)),
    CountsJson String CODEC(ZSTD(1)),
    RequestedBy String CODEC(ZSTD(1)),
    RequestedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (TransactionId)
TTL toDateTime(RequestedAt) + INTERVAL 1095 DAY
SETTINGS index_granularity = 8192;
