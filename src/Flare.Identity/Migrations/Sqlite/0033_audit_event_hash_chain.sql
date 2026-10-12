-- AuditEvents.PrevHash / Hash: tamper-evident hash chain (ADR-0202). Hash is the hex
-- SHA-256 of PrevHash plus the row's own fields; PrevHash is the previous row's Hash
-- (empty string for the first chained row). NULL on rows written before this migration:
-- they are "legacy", outside the chain, and reported as such by the verify endpoint.
ALTER TABLE AuditEvents ADD COLUMN PrevHash TEXT NULL;
ALTER TABLE AuditEvents ADD COLUMN Hash TEXT NULL;
