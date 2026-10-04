-- Reverse-proxy / trusted-header auth settings, configured through the same Admin-only
-- /auth screen as every other method (see docs/auth.md's "Reverse proxy (trusted
-- header)" section) - trusts an identity header an already-authenticating reverse proxy
-- (Authelia, Authentik, oauth2-proxy, Cloudflare Access, Tailscale Serve, ...) injects,
-- instead of Flare talking to an IdP itself. Same settings-singleton shape as
-- EntraSettings/LdapSettings/OidcSettings (CHECK (Id = 1)). No secret column here at
-- all - unlike every other method's settings table, there's nothing to mask.
CREATE TABLE IF NOT EXISTS ProxyAuthSettings
(
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Enabled INTEGER NOT NULL DEFAULT 0,
    HeaderName TEXT NOT NULL DEFAULT 'Remote-User',       -- Grafana's own default header name convention
    TrustedProxyCidrs TEXT NOT NULL DEFAULT '',            -- newline/comma-separated CIDR list, parsed via TrustedProxyNetworks - mandatory (non-empty, at least one entry that actually parses) before this can be Enabled, since a header alone is trivially spoofable by any client reaching Flare.Api directly
    GroupsHeaderName TEXT NULL,                             -- optional second header carrying group membership, e.g. "X-Forwarded-Groups"
    AdminGroup TEXT NULL,                                   -- the header-based equivalent of LdapSettings' three group DNs
    MemberGroup TEXT NULL,
    ViewerGroup TEXT NULL,
    DefaultRole TEXT NOT NULL DEFAULT 'Viewer' CHECK (DefaultRole IN ('Admin', 'Member', 'Viewer')),
    UpdatedAt TEXT NOT NULL
);
