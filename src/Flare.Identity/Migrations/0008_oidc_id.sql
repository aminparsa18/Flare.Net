-- Generic OpenID Connect support (docs/auth.md's "OpenID Connect" section). Same
-- constraint as 0005_ldap_id.sql: Users.AuthProvider is CHECK (AuthProvider IN
-- ('Local', 'Entra', 'ActiveDirectory')), and SQLite has no ALTER TABLE ... ALTER CHECK -
-- broadening a CHECK constraint needs the same documented table-rebuild procedure (create
-- a new table with the desired schema, copy the data across, drop the old table, rename
-- the new one into place). This migration does exactly that, and nothing else - every
-- column, the Username UNIQUE COLLATE NOCASE constraint, and the Role CHECK are carried
-- over unchanged; only AuthProvider's allowed values grow by one ('Oidc').
--
-- Same foreign-key-enforcement caveat 0005_ldap_id.sql already documented:
-- Microsoft.Data.Sqlite enables FK enforcement by default, so the rebuild has to disable
-- it first (before BEGIN - the PRAGMA is a documented no-op inside a transaction) or the
-- DROP TABLE Users fails with "FOREIGN KEY constraint failed" against Sessions.UserId.
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

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
    AuthProvider TEXT NOT NULL DEFAULT 'Local' CHECK (AuthProvider IN ('Local', 'Entra', 'ActiveDirectory', 'Oidc'))
);

INSERT INTO Users_new (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, ExternalId, AuthProvider)
SELECT Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, ExternalId, AuthProvider FROM Users;

DROP TABLE Users;
ALTER TABLE Users_new RENAME TO Users;

-- DROP TABLE removes indexes defined on it - recreate this one on the renamed table,
-- identical to how 0002_entra_id.sql/0005_ldap_id.sql originally defined it.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_AuthProvider_ExternalId ON Users(AuthProvider, ExternalId) WHERE ExternalId IS NOT NULL;

COMMIT;

PRAGMA foreign_keys = ON;
