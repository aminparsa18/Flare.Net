-- Optional expiry on ingest keys (ADR-0207). UTC ISO-8601; NULL = never expires, the default for every existing key.
ALTER TABLE IngestApiKeys ADD COLUMN IF NOT EXISTS ExpiresAt TEXT NULL;
