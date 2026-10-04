-- Optional per-ingest-key ingestion limits (docs-internal/adr/0051-per-ingest-key-ingestion-limits.md).
-- LimitsEnabled is the on/off toggle, kept separate from the four caps so an operator can
-- switch enforcement off without losing the configured numbers. Each cap is independently
-- optional - NULL means "no cap on this dimension", not zero. Counting itself happens in
-- Redis (Flare.Ingest's IngestKeyUsageStore), not here: this table only holds the config.
--
-- Plain ADD COLUMN, same as 0012_proxyauth_logout_redirect_url.sql. Existing keys default
-- to LimitsEnabled = 0, so upgrading changes nothing until an admin sets a limit.
ALTER TABLE IngestApiKeys ADD COLUMN LimitsEnabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE IngestApiKeys ADD COLUMN MaxEventsPerMinute INTEGER NULL;
ALTER TABLE IngestApiKeys ADD COLUMN MaxBytesPerMinute INTEGER NULL;
ALTER TABLE IngestApiKeys ADD COLUMN MaxEventsPerDay INTEGER NULL;
ALTER TABLE IngestApiKeys ADD COLUMN MaxBytesPerDay INTEGER NULL;
