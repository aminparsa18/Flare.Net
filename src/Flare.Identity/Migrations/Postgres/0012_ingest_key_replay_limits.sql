-- Optional per-key caps on browser session replay bytes (docs-internal/adr/0268-browser-replay-byte-limits.md).
-- NULL = no replay-specific cap, the default for every existing key; the key's shared byte caps still apply to replay.
ALTER TABLE IngestApiKeys ADD COLUMN IF NOT EXISTS MaxReplayBytesPerMinute BIGINT NULL;
ALTER TABLE IngestApiKeys ADD COLUMN IF NOT EXISTS MaxReplayBytesPerDay BIGINT NULL;
