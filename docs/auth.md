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

## Pluggability: adding SSO later

v1 ships local username/password only, but the design leaves room for Entra ID/OIDC/AD
without restructuring anything above:

- Authentication is wired through ASP.NET Core's standard multi-scheme
  `AddAuthentication()` model. The cookie scheme
  (`Flare.Identity.Auth.SessionAuthenticationHandler`, registered as
  `FlareSession`) and a future `AddOpenIdConnect()` scheme can coexist — this is exactly
  what that API is built for, not something Flare bolted on.
- The `Users` table already has everything an SSO-provisioned account needs
  (`Id`/`Username`/`Role`). Adding OIDC support would add an *additive* migration (a new
  nullable `ExternalId`/`AuthProvider` column, following the same idempotent-migration
  pattern every file in `src/Flare.Identity/Migrations/` already uses) to map an OIDC
  subject claim to a local user on first login — not a schema redesign.
- `RequireMember`/`RequireAdmin` authorization policies check the resolved
  `ClaimsPrincipal`'s role claim, regardless of which scheme populated it — zero
  endpoint-protection code changes anywhere in `Flare.Api/Program.cs` when a second
  scheme is added.

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
| `Cors:AllowedOrigins:0`, `:1`, … | none | Origin(s) allowed to call `Flare.Api` with credentials (i.e. the dashboard's own origin). Required — `Flare.Api` no longer defaults to `AllowAnyOrigin()`. |

## Backups

The identity SQLite file isn't backed up by any special tooling — include the
`identity-data` volume (docker-compose) or `.data/identity/` directory (local Aspire dev)
in whatever backup story you already have for the `clickhouse-data`/`redis-data`
volumes.
