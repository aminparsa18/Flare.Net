-- Microsoft Entra ID (SSO) settings, configured through the Admin-only Security screen
-- in the dashboard (/security, EntraSettingsEndpoints.cs) instead of docker-compose/.env
-- config - each self-hosted Flare operator creates their own Entra App Registration and
-- pastes the resulting values in here, mirroring Seq's own Security settings page. A
-- settings-singleton table: CHECK (Id = 1) physically prevents more than one row, and no
-- row at all means "never configured" (IEntraSettingsStore.GetAsync returns an
-- all-disabled default in that case, not null).
CREATE TABLE IF NOT EXISTS EntraSettings
(
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Enabled INTEGER NOT NULL DEFAULT 0,
    TenantId TEXT NULL,
    ClientId TEXT NULL,
    ClientSecret TEXT NULL,     -- plaintext, same trust model as the Auth__Entra__ClientSecret env var it replaces (self-hosted, single-tenant, no secrets-at-rest encryption anywhere else in this DB either) - must be reversible (sent to Microsoft on token exchange), unlike IngestApiKeys.KeyHash which only ever needs comparison
    UpdatedAt TEXT NOT NULL
);
