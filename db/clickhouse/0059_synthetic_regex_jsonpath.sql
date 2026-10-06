-- Synthetic regex and JSON-path assertions, migration 0059.
--
-- See `docs-internal/adr/0133-synthetic-regex-jsonpath-assertions.md`. Additive: empty means no assertion, so
-- existing monitors behave as before. Existing numbered migrations are immutable; run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS BodyMatchesRegex String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS JsonPath String DEFAULT '' CODEC(ZSTD(1));
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS JsonPathEquals String DEFAULT '' CODEC(ZSTD(1));
