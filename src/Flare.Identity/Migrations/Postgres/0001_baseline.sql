-- PostgreSQL baseline for the identity store (ADR-0109). Unlike Migrations/Sqlite/, which
-- replays the history of the embedded database (0001-0026), this is the final schema in one
-- file: no Postgres deployment predates it, so there is nothing to upgrade from. A schema
-- change from here on adds 0002_*.sql here AND a matching numbered file under
-- Migrations/Sqlite/ - the two folders must describe the same logical schema, which the
-- store tests (run against both providers) enforce.
--
-- Mapping from the SQLite schema: INTEGER -> BIGINT (the stores read every integer with
-- GetInt64), REAL -> DOUBLE PRECISION, timestamps stay ISO-8601 TEXT exactly as in SQLite
-- (the stores format/parse them themselves, and same-format UTC strings order correctly as
-- text), and SQLite's COLLATE NOCASE columns become unique indexes on LOWER(...) - the
-- stores compare with LOWER() on both sides, so queries behave identically on either engine.

CREATE TABLE IF NOT EXISTS Users
(
    Id TEXT PRIMARY KEY,
    Username TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL CHECK (Role IN ('Admin', 'Member', 'Viewer')),
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    IsDisabled BIGINT NOT NULL DEFAULT 0,
    ExternalId TEXT NULL,
    AuthProvider TEXT NOT NULL DEFAULT 'Local' CHECK (AuthProvider IN ('Local', 'Entra', 'ActiveDirectory', 'Oidc', 'ReverseProxy', 'ServiceAccount'))
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username ON Users (LOWER(Username));
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_AuthProvider_ExternalId ON Users (AuthProvider, ExternalId) WHERE ExternalId IS NOT NULL;

CREATE TABLE IF NOT EXISTS Sessions
(
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL REFERENCES Users (Id),
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Sessions_UserId ON Sessions (UserId);
CREATE INDEX IF NOT EXISTS IX_Sessions_ExpiresAt ON Sessions (ExpiresAt);

CREATE TABLE IF NOT EXISTS IngestApiKeys
(
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    KeyHash TEXT NOT NULL UNIQUE,
    CreatedAt TEXT NOT NULL,
    RevokedAt TEXT NULL,
    LimitsEnabled BIGINT NOT NULL DEFAULT 0,
    MaxEventsPerMinute BIGINT NULL,
    MaxBytesPerMinute BIGINT NULL,
    MaxEventsPerDay BIGINT NULL,
    MaxBytesPerDay BIGINT NULL
);

CREATE TABLE IF NOT EXISTS PersonalAccessTokens
(
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL REFERENCES Users (Id),
    Name TEXT NOT NULL,
    TokenHash TEXT NOT NULL UNIQUE,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NULL,
    LastUsedAt TEXT NULL,
    RevokedAt TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_PersonalAccessTokens_UserId ON PersonalAccessTokens (UserId);

CREATE TABLE IF NOT EXISTS AuthSettings
(
    Id BIGINT PRIMARY KEY CHECK (Id = 1),
    Enabled BIGINT NOT NULL,
    LocalEnabled BIGINT NOT NULL DEFAULT 1,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS EntraSettings
(
    Id BIGINT PRIMARY KEY CHECK (Id = 1),
    Enabled BIGINT NOT NULL DEFAULT 0,
    TenantId TEXT NULL,
    ClientId TEXT NULL,
    ClientSecret TEXT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS LdapSettings
(
    Id BIGINT PRIMARY KEY CHECK (Id = 1),
    Enabled BIGINT NOT NULL DEFAULT 0,
    Host TEXT NULL,
    Port BIGINT NOT NULL DEFAULT 636,
    UseSsl BIGINT NOT NULL DEFAULT 1,
    BaseDn TEXT NULL,
    BindDn TEXT NULL,
    BindPassword TEXT NULL,
    UserSearchFilter TEXT NOT NULL DEFAULT '(&(objectClass=user)(sAMAccountName={0}))',
    UniqueIdAttribute TEXT NOT NULL DEFAULT 'objectGUID',
    AdminGroupDn TEXT NULL,
    MemberGroupDn TEXT NULL,
    ViewerGroupDn TEXT NULL,
    DefaultRole TEXT NOT NULL DEFAULT 'Viewer' CHECK (DefaultRole IN ('Admin', 'Member', 'Viewer')),
    UpdatedAt TEXT NOT NULL,
    PinnedCertificatePem TEXT NULL
);

CREATE TABLE IF NOT EXISTS OidcSettings
(
    Id BIGINT PRIMARY KEY CHECK (Id = 1),
    Enabled BIGINT NOT NULL DEFAULT 0,
    DisplayName TEXT NULL,
    Authority TEXT NULL,
    ClientId TEXT NULL,
    ClientSecret TEXT NULL,
    Scopes TEXT NOT NULL DEFAULT 'openid profile email',
    RoleClaimName TEXT NOT NULL DEFAULT 'roles',
    DefaultRole TEXT NOT NULL DEFAULT 'Viewer' CHECK (DefaultRole IN ('Admin', 'Member', 'Viewer')),
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ProxyAuthSettings
(
    Id BIGINT PRIMARY KEY CHECK (Id = 1),
    Enabled BIGINT NOT NULL DEFAULT 0,
    HeaderName TEXT NOT NULL DEFAULT 'Remote-User',
    TrustedProxyCidrs TEXT NOT NULL DEFAULT '',
    GroupsHeaderName TEXT NULL,
    AdminGroup TEXT NULL,
    MemberGroup TEXT NULL,
    ViewerGroup TEXT NULL,
    DefaultRole TEXT NOT NULL DEFAULT 'Viewer' CHECK (DefaultRole IN ('Admin', 'Member', 'Viewer')),
    UpdatedAt TEXT NOT NULL,
    LogoutRedirectUrl TEXT NULL
);

CREATE TABLE IF NOT EXISTS LoginAttempts
(
    Username TEXT NOT NULL,
    ClientIp TEXT NOT NULL,
    FailedCount BIGINT NOT NULL,
    LastFailedAt TEXT NOT NULL,
    LockedUntil TEXT NULL,
    PRIMARY KEY (Username, ClientIp)
);

CREATE TABLE IF NOT EXISTS ApdexThresholds
(
    ServiceName TEXT PRIMARY KEY,
    ThresholdMs BIGINT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MetricMetadataOverrides
(
    MetricName TEXT PRIMARY KEY,
    Unit TEXT NULL,
    Description TEXT NULL,
    UpdatedAt TEXT NOT NULL,
    TreatAsCounter BIGINT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS AuditEvents
(
    Id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    Timestamp TEXT NOT NULL,
    ActorId TEXT NULL,
    ActorName TEXT NOT NULL,
    ActorKind TEXT NOT NULL,
    Action TEXT NOT NULL,
    ResourceType TEXT NOT NULL,
    ResourceId TEXT NULL,
    Route TEXT NOT NULL,
    StatusCode BIGINT NOT NULL,
    SourceIp TEXT NULL,
    Changes TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_AuditEvents_Timestamp ON AuditEvents (Timestamp);
CREATE INDEX IF NOT EXISTS IX_AuditEvents_Resource ON AuditEvents (ResourceType, ResourceId);
CREATE INDEX IF NOT EXISTS IX_AuditEvents_Actor ON AuditEvents (ActorId);

-- Append-only: rows can be inserted and pruned by retention (DELETE), never edited.
CREATE OR REPLACE FUNCTION AuditEvents_NoUpdate() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'AuditEvents is append-only';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS TR_AuditEvents_NoUpdate ON AuditEvents;
CREATE TRIGGER TR_AuditEvents_NoUpdate BEFORE UPDATE ON AuditEvents
    FOR EACH ROW EXECUTE FUNCTION AuditEvents_NoUpdate();

CREATE TABLE IF NOT EXISTS DashboardPins
(
    UserId TEXT NOT NULL,
    DashboardId TEXT NOT NULL,
    PinnedAt TEXT NOT NULL,
    PRIMARY KEY (UserId, DashboardId)
);

CREATE TABLE IF NOT EXISTS SourceLinks
(
    ServiceName TEXT PRIMARY KEY,
    Provider TEXT NOT NULL,
    RepoUrl TEXT NOT NULL,
    DefaultRef TEXT NOT NULL,
    PathPrefix TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    AccessToken TEXT NULL
);

CREATE TABLE IF NOT EXISTS LlmModelPrices
(
    Model TEXT PRIMARY KEY,
    InputPerMillion DOUBLE PRECISION NOT NULL,
    OutputPerMillion DOUBLE PRECISION NOT NULL,
    UpdatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_LlmModelPrices_Model ON LlmModelPrices (LOWER(Model));

CREATE TABLE IF NOT EXISTS UserPreferences
(
    UserId TEXT NOT NULL,
    Key TEXT NOT NULL,
    Value TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    PRIMARY KEY (UserId, Key)
);

-- Seeds the single AuthSettings row, same as Migrations/Sqlite/0004_auth_settings.sql: auth
-- starts disabled on a fresh install (no users yet, so /api/auth/bootstrap can create the
-- first admin) and enabled if users already exist.
INSERT INTO AuthSettings (Id, Enabled, LocalEnabled, UpdatedAt)
SELECT 1, CASE WHEN EXISTS (SELECT 1 FROM Users) THEN 1 ELSE 0 END, 1, to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
ON CONFLICT (Id) DO NOTHING;
