-- Retention operations, migration 0068: the cold-tier half of a retention request.
--
-- `ColdAfterDays` is the age at which a signal's parts move to the `cold` volume (RustFS, see
-- docs-internal/adr/0144-cold-storage-rustfs.md); 0 = never tiered. Existing rows (and any
-- request that doesn't ask for tiering) read as 0, so migration 0067's history stays valid.
ALTER TABLE clickhousedb.retention_operations ADD COLUMN IF NOT EXISTS ColdAfterDays UInt32 DEFAULT 0;
