-- AuditForwardingCursor: delivery progress per audit forwarding sink (ADR-0203).
CREATE TABLE IF NOT EXISTS AuditForwardingCursor
(
    Sink TEXT PRIMARY KEY,
    LastId BIGINT NOT NULL
);
