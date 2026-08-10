# Auth + multi-user / roles

Flare ships with local username/password accounts and three fixed roles. This is
**multi-user RBAC on one shared self-hosted instance** — everyone logged into a given
Flare deployment sees the same logs/traces/metrics/alert rules/saved views, just with
different permission levels. It is **not** multi-tenant SaaS isolation: there's no
per-user data ownership anywhere, and `alert_rules`/`saved_views` stay exactly as
global/shared as they always were.

## First run

The first time a Flare instance starts with no users yet, the dashboard sends you to
`/setup` instead of `/login` to create the first account, which is always `Admin`. From
then on, `/login` is where everyone signs in — there's no further "setup mode."

## Roles

| Role | Can do |
|---|---|
| `Viewer` | Read logs, traces, metrics, saved views, ingestion/pipeline/indexing status. |
| `Member` | Everything `Viewer` can, plus create/edit/delete/test-fire alert rules. |
| `Admin` | Everything `Member` can, plus manage users and ingest API keys. |

Roles are a fixed three-value enum, not a custom/configurable permission system — see
[`Flare.Identity.Users.UserRole`](../src/Flare.Identity/Users/UserRole.cs).

## How sessions work

Logging in sets an httpOnly session cookie (`flare_session` by default) — not a JWT.
The cookie's value is an opaque, server-side-tracked token; deleting the corresponding
row (logout, or an admin disabling the account) revokes it immediately, which a
self-contained JWT can't do without a denylist. This also means the live-tail
WebSocket "just works" once you're logged in: the browser sends the same cookie on the
WebSocket upgrade request as any other same-site request, no separate token needed.

Sessions default to a 14-day fixed expiry (`Auth:SessionLifetime`), no sliding window.

## Where accounts live

Users, sessions, and ingest API keys are stored in an **embedded SQLite file** — not a
separate database container. This was a deliberate choice: Flare already runs ClickHouse
(log storage) and Redis (the ingest buffer) as backing services, and adding a third
database container for what's a handful of small, low-write tables wasn't worth the
extra resource footprint. Seq (a similar self-hosted, single-binary tool) is the
reference point — it keeps its own config/identity out of a separate database server too.

**Trade-off, stated plainly:** this means `Flare.Api` can only run as a single replica.
SQLite doesn't support multiple processes writing to the same file across a network
filesystem safely, and Flare's SQLite file lives on a local volume (`identity-data` in
`docker-compose.yml`, `.data/identity/` for local Aspire dev), not something horizontally
scaled replicas could safely share. This is a real constraint if the (currently
unscheduled) Kubernetes/Helm roadmap item ever lands and you want to run more than one
`Flare.Api` pod. If that day comes, migrating the four small tables here
(`Users`/`Sessions`/`IngestApiKeys`/`schema_migrations`) to Postgres is a contained,
mechanical follow-up — not a rewrite of anything in this document.

`Flare.Ingest` shares the same SQLite file (read-mostly, just checking ingest API key
hashes) — both processes point at it via `Identity__DbPath`.

## Ingest API keys

Separate from user accounts on purpose: an ingest API key authenticates a *machine* (an
app's OTLP exporter), not a *person*. Tying it to a `Users` row would force every
telemetry-emitting app to be linked to someone's login, which doesn't match how
collectors/exporters are actually operated (one or a few shared keys per environment).

- **Creating a key:** `Admin` only, via `POST /api/ingest-keys` (name it something like
  `"prod-collector"`). The raw key is shown **exactly once**, in that response — Flare
  never stores or displays it again, only its hash. Copy it somewhere safe immediately.
- **Using a key:** send `Authorization: Bearer <key>` on your OTLP exporter (gRPC or
  HTTP — both are checked the same way).
- **Revoking a key:** `DELETE /api/ingest-keys/{id}`. Takes effect within 30 seconds —
  `Flare.Ingest` caches the active-key set in memory and refreshes it on a timer rather
  than hitting SQLite on every ingest request, so a revoked key isn't rejected
  *instantly*, just quickly.
- **Enforcement is opt-in:** `Auth:IngestKeyRequired` defaults to `false`, so upgrading
  an existing deployment doesn't suddenly reject every logger pointed at it. Create at
  least one key, update your exporters to send it, *then* flip
  `Auth:IngestKeyRequired=true` once you're ready to stop accepting anonymous ingest.
- **A second, config-driven mechanism exists for automation:** `Auth:StaticIngestApiKey`
  is a fixed key set via configuration instead of the dashboard, valid alongside (not
  instead of) any keys created through the UI. This is what `Flare.AppHost` (local dev)
  and `Aspire.Hosting.Flare`'s `AddFlare(..., apiKey: ...)` use — "create a key by
  clicking a button in the dashboard" doesn't fit an automated resource-graph-wiring use
  case, where the AppHost itself needs to hand the same value to both `Flare.Ingest` (to
  accept it) and your app's own OTLP exporter (to send it), before either process has
  even started.

## Microsoft Entra ID (SSO)

Local username/password and Entra ID coexist — no deployment is forced to choose.
Authentication is wired through ASP.NET Core's standard multi-scheme
`AddAuthentication()` model: the cookie scheme
(`Flare.Identity.Auth.SessionAuthenticationHandler`, registered as `FlareSession`) stays
the *only* scheme any endpoint's `RequireAuthorization()` ever actually resolves a
principal through. Entra ID is a second, separate front door that ends in exactly the
same kind of session — `RequireMember`/`RequireAdmin` and every existing endpoint needed
zero changes to support it.

**Single-tenant only.** Flare validates against one specific Entra directory
(`Auth:Entra:TenantId`), not `common`/`organizations` — letting any Entra org's users
reach a self-hosted internal tool's login is the wrong default.

### How it works

1. The dashboard's "Sign in with Microsoft" button (shown only when
   `GET /api/auth/bootstrap/status` reports `entraEnabled: true`) navigates the browser
   to `GET /api/auth/entra/login`, which challenges the `Entra` OpenIdConnect scheme.
2. You sign in at Microsoft's own page. Entra redirects back to `/signin-oidc` (the OIDC
   handler's default callback path, handled by framework middleware), which validates
   the token and hands off to `GET /api/auth/entra/complete`.
3. `complete` looks up the account by the token's `oid` claim
   (`Users.AuthProvider = 'Entra'`, `Users.ExternalId = <oid>`). **First-ever login for
   that identity** creates the row — see "Role provisioning" below for how its role is
   picked. **Every login after that** just signs in as the existing row, role unchanged.
4. A normal `flare_session` cookie is minted — same mechanism, same cookie, same
   14-day-fixed-expiry behavior as a password login. From here on an Entra-provisioned
   session is indistinguishable from a local one to every other endpoint in the app.

### Role provisioning

Entra ID **App Roles** are the role source, matched by name against Flare's own
`Admin`/`Member`/`Viewer` enum:

1. In the Entra App Registration's manifest, add three `appRoles` entries with
   `"value"` set to exactly `Admin`, `Member`, and `Viewer` (case-insensitive match, but
   keep them exact) and `"allowedMemberTypes": ["User"]`.
2. In **Enterprise applications → your app → Users and groups**, assign each person (or
   group) the one App Role that should become their Flare role.
3. On that person's **first** sign-in, Flare reads the token's `roles` claim and picks
   the highest-privilege match (`Admin` > `Member` > `Viewer`). No App Role assigned (or
   App Roles not configured on the registration at all) provisions
   `Auth:Entra:DefaultRole` instead — `Viewer` unless you've overridden it.
4. **Role changes after that live in Flare, not Entra.** Continuously re-reading the
   `roles` claim on every login would make the Users screen's own role control
   meaningless for SSO accounts — see "Managing users" below to promote/demote an
   Entra-provisioned account exactly like a local one.

A disabled account (local or Entra) can't sign in either way — an Entra sign-in for a
disabled account bounces back to `/login?error=account-disabled` instead of getting a
session.

### App Registration setup (Azure Portal)

1. **Entra ID → App registrations → New registration.** Single tenant
   ("Accounts in this organizational directory only").
2. **Redirect URI**: platform "Web", value `http://localhost:8080/signin-oidc` for a
   local `docker compose up` (substitute your real `FLARE_API_PORT`/host for anything
   beyond localhost — and use `https://` once this is reachable beyond your own machine,
   see the caveat below).
3. **Certificates & secrets → New client secret.** Copy the value immediately — like an
   ingest API key's raw value, Azure only shows it once.
4. **App roles** (see "Role provisioning" above) and, if you want anyone to actually be
   assigned one, **Enterprise applications → your app → Users and groups**.
5. Note the **Application (client) ID** and **Directory (tenant) ID** from the
   registration's Overview page.
6. Set `Auth:Entra:Enabled=true`, `Auth:Entra:TenantId`, `Auth:Entra:ClientId`,
   `Auth:Entra:ClientSecret` (see the config reference below — `.env`'s
   `ENTRA_*` variables for `docker compose`).

**HTTPS caveat:** the correlation cookies ASP.NET Core's OIDC handler sets during the
redirect round-trip to Microsoft need to survive a cross-site navigation, which browsers
only reliably allow over HTTPS (Chrome/Edge special-case plain `http://localhost` for
this, which is why local dev works). Any real deployment reachable beyond your own
machine needs to be behind HTTPS for Entra ID sign-in to work, same as it already should
be for `Auth:CookieSecure`.

### Managing users

`Admin`-only, at `/users` in the dashboard (`GET`/`PATCH /api/users/*` in the API) —
list every account (local and Entra alike), change a role, or enable/disable an account.
This is also where you promote a newly-auto-provisioned Entra account past its initial
role, or disable one without waiting for someone to remove their Entra App Role
assignment. Flare refuses to demote or disable the **last enabled Admin** — that would
be a lockout recoverable only by editing the SQLite file directly.

## Configuration reference

| Key | Default | What it does |
|---|---|---|
| `Identity:DbPath` | `flare-identity.db` | Path to the shared SQLite file. Set to a volume-backed absolute path in any real deployment — `docker-compose.yml` and `Flare.AppHost` already do this for you. |
| `Auth:CookieName` | `flare_session` | Session cookie name. |
| `Auth:SessionLifetime` | `14.00:00:00` (14 days) | Fixed session expiry, set at login. |
| `Auth:CookieSecure` | `true` | Set `false` only for local plain-HTTP dev. |
| `Auth:CookieSameSite` | `Lax` | `None` (with `CookieSecure=true`) if your dashboard and API are ever split across genuinely different domains, not just different ports on `localhost`. |
| `Auth:IngestKeyRequired` | `false` | Whether `Flare.Ingest` rejects OTLP requests with no valid API key. |
| `Auth:StaticIngestApiKey` | unset | A fixed ingest key set via config instead of the dashboard — see "Ingest API keys" above. |
| `Cors:AllowedOrigins:0`, `:1`, … | none | Origin(s) allowed to call `Flare.Api` with credentials (i.e. the dashboard's own origin). Required — `Flare.Api` no longer defaults to `AllowAnyOrigin()`. Also doubles as the Entra login `returnUrl` allow-list. |
| `Auth:Entra:Enabled` | `false` | Registers the `Entra` OpenIdConnect scheme and the "Sign in with Microsoft" flow. Requires `TenantId`/`ClientId`/`ClientSecret` below to actually work. |
| `Auth:Entra:TenantId` | unset | The Entra App Registration's Directory (tenant) ID. Single-tenant only — see above. |
| `Auth:Entra:ClientId` | unset | The App Registration's Application (client) ID. |
| `Auth:Entra:ClientSecret` | unset | The App Registration's client secret. No working default — same "blank until configured" convention as the alerting Email channel's SMTP settings. |
| `Auth:Entra:DefaultRole` | `Viewer` | Role assigned on first login when the token carries no recognized `roles` claim entry. |

## Backups

The identity SQLite file isn't backed up by any special tooling — include the
`identity-data` volume (docker-compose) or `.data/identity/` directory (local Aspire dev)
in whatever backup story you already have for the `clickhouse-data`/`redis-data`
volumes.
