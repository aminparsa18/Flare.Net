-- The global authentication on/off switch (Planning.md's "opt-in auth" pivot,
-- docs/auth.md). A fresh Flare install has no auth requirement at all - anyone who can
-- reach the dashboard gets full access - until an Admin explicitly turns it on from the
-- /auth screen. Same settings-singleton shape as EntraSettings/LdapSettings
-- (CHECK (Id = 1)).
CREATE TABLE IF NOT EXISTS AuthSettings
(
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Enabled INTEGER NOT NULL,
    LocalEnabled INTEGER NOT NULL DEFAULT 1,   -- local username/password is its own toggle now too, for symmetry with Entra/LDAP's Enabled flags - defaults on since it's the natural first/bootstrap method
    UpdatedAt TEXT NOT NULL
);

-- Backward-compat seed, not just a column default: an *existing* database that already
-- has Users rows (a v11/v12 deployment applying this migration on upgrade) must keep
-- requiring auth - only a genuinely fresh install (no Users yet) gets Enabled=0. Getting
-- this backwards would silently strip auth from every already-secured instance on
-- upgrade. Runs once (IdentityMigrationRunner tracks this file as applied), so the
-- WHERE NOT EXISTS guard is defense-in-depth, not the actual idempotency mechanism.
INSERT INTO AuthSettings (Id, Enabled, LocalEnabled, UpdatedAt)
SELECT 1, EXISTS(SELECT 1 FROM Users), 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE NOT EXISTS (SELECT 1 FROM AuthSettings WHERE Id = 1);
