-- AuditEvents.PrevHash / Hash: tamper-evident hash chain (ADR-0202). See the Sqlite
-- migration of the same name.
ALTER TABLE AuditEvents ADD COLUMN IF NOT EXISTS PrevHash TEXT NULL;
ALTER TABLE AuditEvents ADD COLUMN IF NOT EXISTS Hash TEXT NULL;
