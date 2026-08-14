-- Generic OpenID Connect settings, configured through the same Admin-only /auth screen
-- as local auth/Entra ID/LDAP (see docs/auth.md's "OpenID Connect" section) - each
-- self-hosted Flare operator points this at their own standards-compliant OIDC provider
-- (Okta, Auth0, Keycloak, Authentik, ...), not tied to Microsoft's authority URL pattern
-- the way EntraSettings is. Same settings-singleton shape as EntraSettings/LdapSettings
-- (CHECK (Id = 1)).
CREATE TABLE IF NOT EXISTS OidcSettings
(
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Enabled INTEGER NOT NULL DEFAULT 0,
    DisplayName TEXT NULL,                          -- drives the /login page's "Sign in with {DisplayName}" button label - no fixed brand like Entra's "Microsoft" for a generic provider
    Authority TEXT NULL,                            -- the OIDC provider's issuer URL, e.g. "https://your-tenant.okta.com" - discovery document is fetched from "{Authority}/.well-known/openid-configuration"
    ClientId TEXT NULL,
    ClientSecret TEXT NULL,                         -- plaintext, same trust model as EntraSettings.ClientSecret/LdapSettings.BindPassword (self-hosted, single-tenant, has to be reversible to actually exchange it)
    Scopes TEXT NOT NULL DEFAULT 'openid profile email',
    RoleClaimName TEXT NOT NULL DEFAULT 'roles',    -- configurable, unlike Entra's hardcoded "roles" App Role claim - arbitrary providers vary in what claim (if any) carries role/group info
    DefaultRole TEXT NOT NULL DEFAULT 'Viewer' CHECK (DefaultRole IN ('Admin', 'Member', 'Viewer')),  -- deliberately DB-bound, not config-bound like Entra's DefaultRole - same reasoning LdapSettings.DefaultRole already established
    UpdatedAt TEXT NOT NULL
);
