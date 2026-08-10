-- Users: local username/password accounts for Flare's multi-user RBAC (Planning.md's
-- "Auth + multi-user / roles" item). Single shared self-hosted instance, not
-- multi-tenant - there is deliberately no TenantId/OrgId column anywhere in this schema
-- (see docs/auth.md). Role is a plain enum column, not a Roles/UserRoles join table -
-- three fixed roles isn't worth normalizing; revisit only if custom roles become a real
-- requirement.
CREATE TABLE IF NOT EXISTS Users
(
    Id TEXT PRIMARY KEY,               -- GUID string, mirrors ClickHouse's `Id UUID` convention (db/clickhouse/0003_alert_rules.sql etc.)
    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash TEXT NOT NULL,        -- Microsoft.AspNetCore.Identity.PasswordHasher<T>'s versioned format (PBKDF2-HMAC-SHA256) - see Auth/AspNetPasswordHasher.cs
    Role TEXT NOT NULL CHECK (Role IN ('Admin', 'Member', 'Viewer')),
    CreatedAt TEXT NOT NULL,           -- ISO-8601 UTC (SQLite has no native datetime type - stored as TEXT, same convention as the other TEXT timestamp columns below)
    UpdatedAt TEXT NOT NULL,
    IsDisabled INTEGER NOT NULL DEFAULT 0  -- 0/1; soft-disable instead of DELETE, keeps Sessions' FK-free audit trail simple
);

-- Sessions: opaque server-side session tokens (not JWT - see docs/auth.md for the
-- rationale: revocation needs a server-side row anyway, and Flare.Api is already
-- single-process with this SQLite decision, so JWT's stateless-scaling payoff doesn't
-- apply). Deleting a row revokes that session immediately.
CREATE TABLE IF NOT EXISTS Sessions
(
    Id TEXT PRIMARY KEY,               -- the opaque session token itself (256-bit CSPRNG, base64url) - doubles as both the cookie value and this row's key
    UserId TEXT NOT NULL REFERENCES Users(Id),
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL           -- updated opportunistically (throttled, not on every request) for an admin-facing "last active" display only - not used to compute expiry
);
CREATE INDEX IF NOT EXISTS IX_Sessions_UserId ON Sessions(UserId);
CREATE INDEX IF NOT EXISTS IX_Sessions_ExpiresAt ON Sessions(ExpiresAt);  -- for a periodic expired-session cleanup sweep

-- IngestApiKeys: authenticate OTLP-exporting *machines*, deliberately not tied to any
-- Users row (see docs/auth.md - conflating the two would force every telemetry-emitting
-- app to be tied to a human login, which doesn't match how collectors/exporters are
-- actually operated). One or more named, independently-revocable keys.
CREATE TABLE IF NOT EXISTS IngestApiKeys
(
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,                -- operator-facing label, e.g. "prod-collector"
    KeyHash TEXT NOT NULL UNIQUE,      -- SHA-256 of the raw key - not PBKDF2; this is a high-QPS per-request check on the ingest hot path, not a login, and a 256-bit random key has no brute-force surface to slow down
    CreatedAt TEXT NOT NULL,
    RevokedAt TEXT NULL                -- NULL = active; set instead of DELETE, keeps an audit trail
);
