-- Active Directory (LDAP) settings, configured through the same Admin-only /auth screen
-- as Entra ID/local auth (see docs/auth.md's "Active Directory (LDAP)" section) - each
-- self-hosted Flare operator points this at their own AD/AD-compatible directory. Same
-- settings-singleton shape as EntraSettings/AuthSettings (CHECK (Id = 1)).
CREATE TABLE IF NOT EXISTS LdapSettings
(
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Enabled INTEGER NOT NULL DEFAULT 0,
    Host TEXT NULL,
    Port INTEGER NOT NULL DEFAULT 636,             -- LDAPS by default (see UseSsl) - 389 is the plaintext-LDAP convention if an operator explicitly turns UseSsl off
    UseSsl INTEGER NOT NULL DEFAULT 1,
    BaseDn TEXT NULL,                              -- search base, e.g. "DC=corp,DC=example,DC=com"
    BindDn TEXT NULL,                               -- service account DN used to search for the submitted username (see IsLdapAuthEndpoints' search-then-bind remarks)
    BindPassword TEXT NULL,                        -- plaintext, same trust model as EntraSettings.ClientSecret (self-hosted, single-tenant, has to be reversible to actually bind with it)
    UserSearchFilter TEXT NOT NULL DEFAULT '(&(objectClass=user)(sAMAccountName={0}))',
    UniqueIdAttribute TEXT NOT NULL DEFAULT 'objectGUID',  -- AD's stable per-object identifier - a field, not hardcoded, so a Samba AD/other AD-compatible directory with a different attribute name still works
    AdminGroupDn TEXT NULL,                        -- the AD-native equivalent of Entra's three App Roles
    MemberGroupDn TEXT NULL,
    ViewerGroupDn TEXT NULL,
    DefaultRole TEXT NOT NULL DEFAULT 'Viewer' CHECK (DefaultRole IN ('Admin', 'Member', 'Viewer')),  -- deliberately DB-bound, not config-bound like Entra's DefaultRole - the three group DNs above are already Flare-UI-configured data sitting right next to it
    UpdatedAt TEXT NOT NULL
);
