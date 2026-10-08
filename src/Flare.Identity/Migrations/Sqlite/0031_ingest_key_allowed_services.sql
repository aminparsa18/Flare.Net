-- Service allowlist on ingest keys (ADR-0150). Newline-separated service.name values;
-- NULL = unrestricted, the default for every existing key.
ALTER TABLE IngestApiKeys ADD COLUMN AllowedServices TEXT NULL;
