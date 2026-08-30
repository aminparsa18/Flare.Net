# Authentication configuration reference

Exact roles, identity-resolution facts, and config keys for Flare's
authentication system. For what each method actually does, see
[`../explanation/authentication-model.md`](../explanation/authentication-model.md).
For setup steps, see
[`../how-to/configure-authentication.md`](../how-to/configure-authentication.md).

## Roles

| Role | Can do |
|---|---|
| `Viewer` | Read logs, traces, metrics, saved views, ingestion/pipeline/indexing status. |
| `Member` | Everything `Viewer` can, plus create/edit/delete/test-fire alert rules. |
| `Admin` | Everything `Member` can, plus manage users and ingest API keys. |

Fixed three-value enum, not a custom/configurable permission system — see
[`Flare.Identity.Users.UserRole`](../../src/Flare.Identity/Users/UserRole.cs).

## Identity resolution per method

| Method | `Users.AuthProvider` | External identity source | Role source | Restart required on settings change? |
|---|---|---|---|---|
| Local | `Local` | username | set directly on the account | No |
| Microsoft Entra ID | `Entra` | token `oid` claim | Entra App Roles (`roles` claim) | Yes |
| Active Directory | `ActiveDirectory` | `UniqueIdAttribute` (default `objectGUID`) | 3 configurable group DNs (`memberOf`) | No |
| OpenID Connect | `Oidc` | token `sub` claim | configurable claim name (default `roles`) | Yes |
| Reverse proxy | `ReverseProxy` | configured header's value | optional groups header, 3 configurable group names | No |

A disabled account (any provider) is refused a session — Entra/OIDC bounce
to `/login?error=account-disabled`; LDAP/reverse-proxy return the same
generic `401` a wrong password gets.

## Configuration reference

| Key | Default | What it does |
|---|---|---|
| `Identity:DbPath` | `flare-identity.db` | Path to the shared SQLite file. Set to a volume-backed absolute path in any real deployment — `docker-compose.yml` and `Flare.AppHost` already do this for you. |
| `Auth:CookieName` | `flare_session` | Session cookie name. |
| `Auth:SessionLifetime` | `14.00:00:00` (14 days) | Fixed session expiry, set at login. |
| `Auth:CookieSecure` | `true` | Set `false` only for local plain-HTTP dev. |
| `Auth:CookieSameSite` | `Lax` | `None` (with `CookieSecure=true`) if your dashboard and API are ever split across genuinely different domains, not just different ports on `localhost`. |
| `Auth:IngestKeyRequired` | `false` | Whether `Flare.Ingest` rejects OTLP requests with no valid API key. |
| `Auth:StaticIngestApiKey` | unset | A fixed ingest key set via config instead of the dashboard — see [ingest API keys](../how-to/configure-authentication.md#ingest-api-keys). |
| `Cors:AllowedOrigins:0`, `:1`, … | none | Origin(s) allowed to call `Flare.Api` with credentials (i.e. the dashboard's own origin). Required — `Flare.Api` no longer defaults to `AllowAnyOrigin()`. Also doubles as the Entra login `returnUrl` allow-list. |
| `Auth:Entra:DefaultRole` | `Viewer` | Role assigned on first login when the token carries no recognized `roles` claim entry. The one Entra-related setting that's still config-bound — `Enabled`/`TenantId`/`ClientId`/`ClientSecret` live in the database instead, set via the `/auth` page (Admin-only, `GET`/`PUT /api/settings/entra`). |

**Everything else has no configuration-file equivalent at all** — set
exclusively through `/auth`:

- The global "Require sign-in" switch, `LocalEnabled`
- Every Active Directory setting (`Host`/`Port`/`BaseDn`/`BindDn`/
  `BindPassword`/group DNs/`DefaultRole`/etc.) —
  `GET`/`PUT /api/settings/ldap`
- Every OpenID Connect setting (`Authority`/`ClientId`/`ClientSecret`/
  `Scopes`/`RoleClaimName`/`DefaultRole`) — `GET`/`PUT /api/settings/oidc`
- Every reverse-proxy setting (`HeaderName`/`TrustedProxyCidrs`/
  `GroupsHeaderName`/group names/`DefaultRole`) —
  `GET`/`PUT /api/settings/proxyauth`

See [ADR-0004](../../docs-internal/adr/0004-embedded-sqlite-for-identity.md)
for why this is database-backed rather than config-file-backed.