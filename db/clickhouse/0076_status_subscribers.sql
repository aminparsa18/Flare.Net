-- Status page visitor subscriptions schema, migration 0076.
--
-- `status_subscribers` (Flare.Api.Model.StatusPageModels.cs): an email address that asked to hear about a
-- status page's incidents. `Id` is derived from the page and the lower-cased address, so subscribing twice
-- writes the same row. `Verified` flips once the address follows its confirmation link. See
-- `docs-internal/adr/0162-status-page-visitor-subscriptions.md`. Same CRUD-via-tombstone shape as
-- `status_pages` (migration 0072).
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0075, run this by hand via `clickhouse-client`
-- against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.status_subscribers
(
    Id UUID,
    PageId UUID,
    Email String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Verified UInt8 DEFAULT 0,

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    -- Also when the last confirmation email went out while unverified (the resend cooldown).
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
