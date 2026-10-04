-- PersonalAccessTokens: durable, user-scoped bearer tokens for programmatic access to
-- Flare.Api's query side (/api/logs, /api/alerts, etc.) - the counterpart IngestApiKeys
-- (0001_identity.sql) deliberately doesn't cover, since those authenticate
-- telemetry-emitting machines, not people. A PAT authenticates as the User it belongs to
-- (same role, same permissions a browser session for that user would have) so a
-- script/CI job/another service can call the query API without impersonating a browser
-- session. See docs-internal/adr/0019-personal-access-tokens.md.
CREATE TABLE IF NOT EXISTS PersonalAccessTokens
(
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL REFERENCES Users(Id),
    Name TEXT NOT NULL,                -- operator-facing label, e.g. "ci-pipeline"
    TokenHash TEXT NOT NULL UNIQUE,    -- SHA-256 of the raw token - same rationale as IngestApiKeys.KeyHash
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NULL,               -- NULL = never expires
    LastUsedAt TEXT NULL,              -- updated opportunistically (throttled, not on every request) - admin-facing display only
    RevokedAt TEXT NULL                -- NULL = active; set instead of DELETE, keeps an audit trail
);
CREATE INDEX IF NOT EXISTS IX_PersonalAccessTokens_UserId ON PersonalAccessTokens(UserId);
