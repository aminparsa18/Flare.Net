-- AuditForwardingCursor: how far each audit forwarding sink has delivered (ADR-0203).
-- LastId is the highest AuditEvents.Id the sink acknowledged; delivery is at-least-once.
CREATE TABLE IF NOT EXISTS AuditForwardingCursor
(
    Sink TEXT PRIMARY KEY,
    LastId INTEGER NOT NULL
);
