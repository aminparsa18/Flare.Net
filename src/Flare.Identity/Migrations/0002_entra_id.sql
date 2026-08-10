-- Entra ID (SSO) support (Planning.md's "Entra ID (SSO) auth" item, docs/auth.md's
-- "Pluggability: adding SSO later" section this fulfills). Purely additive, exactly as
-- that section already promised - no existing column touched, no table rebuilt.
--
-- PasswordHash stays NOT NULL (SQLite can't cheaply relax a column constraint without a
-- full table rebuild). An Entra-provisioned row still gets a PasswordHash value, just one
-- generated from a random, never-revealed string via the same AspNetPasswordHasher local
-- accounts use - a well-formed hash that legitimately verifies as Failed against any real
-- password, so the local /login form simply rejects an Entra account's username instead
-- of needing a schema change to represent "no password".
ALTER TABLE Users ADD COLUMN ExternalId TEXT NULL;
ALTER TABLE Users ADD COLUMN AuthProvider TEXT NOT NULL DEFAULT 'Local' CHECK (AuthProvider IN ('Local', 'Entra'));

-- One external identity maps to at most one Flare user, per provider. NULL ExternalId
-- (every existing local account) is excluded - SQLite's UNIQUE index already treats
-- distinct NULLs as non-conflicting, but the WHERE clause makes that explicit rather than
-- relying on the reader already knowing SQLite's NULL-uniqueness semantics.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_AuthProvider_ExternalId ON Users(AuthProvider, ExternalId) WHERE ExternalId IS NOT NULL;
