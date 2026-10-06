-- Synthetic probe locations, migration 0058.
--
-- Which probe locations run a monitor. See `docs-internal/adr/0130-synthetic-multi-location.md`. Additive: an
-- empty array means "every location", so existing monitors keep running wherever a worker is. Existing numbered
-- migrations are immutable; run this by hand via `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.synthetic_monitors ADD COLUMN IF NOT EXISTS Locations Array(String) DEFAULT [] CODEC(ZSTD(1));
