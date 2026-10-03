-- Service accounts (ADR-0082): a non-human principal an Admin creates for CI, scripts and
-- other services, authenticating only through personal access tokens. Stored as a Users row
-- with AuthProvider = 'ServiceAccount' so PAT auth, roles, disabling and per-token rate
-- limits all work unchanged. Broadening Users.AuthProvider's CHECK needs the same table
-- rebuild as 0005/0008/0010 (see 0010_proxyauth_id.sql for the foreign-key and
-- transaction notes); nothing else about the table changes.

CREATE TABLE Users_new
(
    Id TEXT PRIMARY KEY,
    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL CHECK (Role IN ('Admin', 'Member', 'Viewer')),
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    IsDisabled INTEGER NOT NULL DEFAULT 0,
    ExternalId TEXT NULL,
    AuthProvider TEXT NOT NULL DEFAULT 'Local' CHECK (AuthProvider IN ('Local', 'Entra', 'ActiveDirectory', 'Oidc', 'ReverseProxy', 'ServiceAccount'))
);

INSERT INTO Users_new (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, ExternalId, AuthProvider)
SELECT Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, ExternalId, AuthProvider FROM Users;

DROP TABLE Users;
ALTER TABLE Users_new RENAME TO Users;

-- DROP TABLE removes indexes defined on it - recreate this one on the renamed table,
-- identical to how 0002_entra_id.sql/0005_ldap_id.sql/0008_oidc_id.sql originally
-- defined it.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_AuthProvider_ExternalId ON Users(AuthProvider, ExternalId) WHERE ExternalId IS NOT NULL;
