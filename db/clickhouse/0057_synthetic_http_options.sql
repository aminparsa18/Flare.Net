-- Synthetic HTTP probe options, migration 0057.
--
-- Request headers, a request body and response-body assertions for Http monitors. See
-- `docs-internal/adr/0129-synthetic-http-assertions.md`. Additive, so existing monitors keep their behaviour
-- (empty means "none"). Existing numbered migrations are immutable; run this by hand via `clickhouse-client`
-- against any already-running instance.

-- One `Name: value` header per line.
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS RequestHeaders String DEFAULT '' CODEC(ZSTD(1));

-- Sent with POST only.
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS RequestBody String DEFAULT '' CODEC(ZSTD(1));

-- Up only when the response body contains / does not contain this text. Empty means no assertion.
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS BodyContains String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS BodyNotContains String DEFAULT '' CODEC(ZSTD(1));
