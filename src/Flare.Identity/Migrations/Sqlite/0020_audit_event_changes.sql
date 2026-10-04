-- AuditEvents.Changes: JSON array of {field, before, after} for updates (ADR-0079
-- follow-up, ADR-0081). Values of secret-looking fields are already redacted by the time
-- they are written; NULL for events with no field-level change to show (creates, deletes,
-- and updates by handlers that don't report a before/after).
ALTER TABLE AuditEvents ADD COLUMN Changes TEXT NULL;
