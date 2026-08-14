-- Active Directory (LDAP) support (Planning.md's "Active Directory auth" item,
-- docs/auth.md). Unlike 0002_entra_id.sql, this one genuinely can't stay purely
-- additive: Users.AuthProvider is CHECK (AuthProvider IN ('Local', 'Entra')), and SQLite
-- has no ALTER TABLE ... ALTER CHECK - broadening a CHECK constraint needs the
-- documented table-rebuild procedure (create a new table with the desired schema, copy
-- the data across, drop the old table, rename the new one into place), not a simple
-- ADD COLUMN. This migration does exactly that, and nothing else - every column, the
-- Username UNIQUE COLLATE NOCASE constraint, and the Role CHECK are carried over
-- unchanged; only AuthProvider's allowed values grow by one.
--
-- Found the hard way (a real test, not a hunch): Microsoft.Data.Sqlite enables foreign
-- key enforcement by default on every connection, unlike SQLite itself (whose own
-- default is off) - Sessions.UserId REFERENCES Users(Id) is genuinely enforced here, and
-- a bare DROP TABLE Users while it's still referenced fails with "FOREIGN KEY constraint
-- failed". SQLite's own documented rebuild recipe exists precisely for this: disable
-- enforcement first - and that PRAGMA is a documented no-op if issued *inside* a
-- transaction, so it has to come before BEGIN, not after.
PRAGMA foreign_keys = OFF;

-- Wrapped in an explicit transaction - unlike every prior migration here (all pure
-- idempotent CREATE-IF-NOT-EXISTS DDL, safe to fail/retry mid-file), this one drops the
-- real Users table partway through; an interruption between DROP and RENAME with no
-- transaction would leave the database with no Users table at all.
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
    AuthProvider TEXT NOT NULL DEFAULT 'Local' CHECK (AuthProvider IN ('Local', 'Entra', 'ActiveDirectory'))
);

INSERT INTO Users_new (Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, ExternalId, AuthProvider)
SELECT Id, Username, PasswordHash, Role, CreatedAt, UpdatedAt, IsDisabled, ExternalId, AuthProvider FROM Users;

DROP TABLE Users;
ALTER TABLE Users_new RENAME TO Users;

-- DROP TABLE removes indexes defined on it - recreate this one on the renamed table,
-- identical to how 0002_entra_id.sql originally defined it.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_AuthProvider_ExternalId ON Users(AuthProvider, ExternalId) WHERE ExternalId IS NOT NULL;

COMMIT;

PRAGMA foreign_keys = ON;
