-- Project ownership on ingest keys (ADR-0123 phase 3). NULL = instance-wide, the default for
-- every existing key. No foreign key, matching the ClickHouse config objects' ProjectId: a
-- deleted project leaves its keys as-is until an admin reassigns them.
ALTER TABLE IngestApiKeys ADD COLUMN IF NOT EXISTS ProjectId TEXT NULL;
