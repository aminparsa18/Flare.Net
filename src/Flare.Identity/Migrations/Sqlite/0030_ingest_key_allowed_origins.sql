-- Browser-origin allowlist on ingest keys (ADR-0149). Newline-separated normalized origins
-- (scheme://host[:port]); NULL = unrestricted, the default for every existing key.
ALTER TABLE IngestApiKeys ADD COLUMN AllowedOrigins TEXT NULL;
