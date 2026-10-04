-- AuditEvents: append-only record of who changed what and when (ADR-0079). Written by
-- Flare.Api's audit middleware for every successful state-changing request; read by the
-- admin-only Audit log page. The Id AUTOINCREMENT is the keyset-pagination cursor
-- (newest first), so it is never reused after a retention prune.
--
-- No FOREIGN KEY to Users(Id): the row must outlive the account it names (a deleted or
-- renamed user is exactly when you want to know who they were), so the actor's username
-- is copied in at write time. ActorId is NULL for requests with no resolvable user.
-- ResourceId is NULL for creates whose id is only known to the handler and wasn't
-- reported back, and for resources keyed by nothing (singleton settings).
CREATE TABLE IF NOT EXISTS AuditEvents
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    ActorId TEXT NULL,
    ActorName TEXT NOT NULL,
    -- 'session' or 'pat' - how the actor authenticated.
    ActorKind TEXT NOT NULL,
    -- 'create' | 'update' | 'delete' | 'revoke' | ...
    Action TEXT NOT NULL,
    -- 'alert' | 'dashboard' | 'user' | 'auth-settings' | ...
    ResourceType TEXT NOT NULL,
    ResourceId TEXT NULL,
    -- HTTP method + route template, e.g. 'PUT /api/alerts/{id:guid}'.
    Route TEXT NOT NULL,
    StatusCode INTEGER NOT NULL,
    SourceIp TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_AuditEvents_Timestamp ON AuditEvents (Timestamp);
CREATE INDEX IF NOT EXISTS IX_AuditEvents_Resource ON AuditEvents (ResourceType, ResourceId);
CREATE INDEX IF NOT EXISTS IX_AuditEvents_Actor ON AuditEvents (ActorId);

-- Append-only: rows can be inserted and pruned by retention (DELETE), never edited.
CREATE TRIGGER IF NOT EXISTS TR_AuditEvents_NoUpdate
BEFORE UPDATE ON AuditEvents
BEGIN
    SELECT RAISE(ABORT, 'AuditEvents is append-only');
END;
