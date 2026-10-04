-- Single-use, expiring set-password tokens (invite + admin-generated reset links). Only the
-- SHA-256 of the raw token is stored. One live token per user: creating a new one deletes
-- the old.
CREATE TABLE IF NOT EXISTS PasswordSetTokens
(
    TokenHash TEXT PRIMARY KEY,
    UserId TEXT NOT NULL REFERENCES Users (Id),
    Purpose TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_PasswordSetTokens_UserId ON PasswordSetTokens (UserId);
