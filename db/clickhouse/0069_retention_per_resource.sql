-- Per-resource retention, migration 0069 (docs-internal/adr/0145-per-resource-retention.md).
--
-- `_retention_days` is how long a row is kept, computed at insert time from the retention rules
-- (`multiIf(ResourceAttributes['deployment.environment'] = 'dev', 7, 30)`), so one table can hold
-- rows with different lifetimes. Flare.Api rewrites this column's DEFAULT when rules change and
-- points the table TTL at it (`... + toIntervalDay(_retention_days)`); until then nothing
-- references it.
--
-- The default is 20000 (about 55 years, "keep forever"; see RetentionSql.ForeverDays for why not more), never 0: a TTL of `+ 0 days` would delete a
-- row the moment it is evaluated, and rows written before any rule exists must not be at risk.
-- Existing rows don't need a backfill: a DEFAULT column is computed from the current expression
-- when a part that lacks it is read or merged.
ALTER TABLE clickhousedb.logs ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;
ALTER TABLE clickhousedb.spans ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;
ALTER TABLE clickhousedb.metrics_gauge ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;
ALTER TABLE clickhousedb.metrics_sum ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;
ALTER TABLE clickhousedb.metrics_histogram ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;
ALTER TABLE clickhousedb.metrics_exponential_histogram ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;
ALTER TABLE clickhousedb.profile_samples ADD COLUMN IF NOT EXISTS _retention_days UInt16 DEFAULT 20000;

-- The per-signal rules last requested, as JSON, next to the day counts they accompany.
ALTER TABLE clickhousedb.retention_operations ADD COLUMN IF NOT EXISTS RulesJson String DEFAULT '' CODEC(ZSTD(1));
