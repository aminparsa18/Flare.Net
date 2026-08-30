# Authentication model

Flare ships with local username/password accounts, Microsoft Entra ID
(SSO), Active Directory (LDAP), generic OpenID Connect, and reverse-proxy
(trusted header) sign-in, plus three fixed roles. This is **multi-user RBAC
on one shared self-hosted instance** — everyone logged into a given Flare
deployment sees the same logs/traces/metrics/alert rules/saved views, just
with different permission levels. It is **not** multi-tenant SaaS
isolation: there's no per-user data ownership anywhere, and
`alert_rules`/`saved_views` stay exactly as global/shared as they always
were.

For setting any of this up, see
[`../how-to/configure-authentication.md`](../how-to/configure-authentication.md).
For exact config keys and the roles table, see
[`../reference/authentication-config.md`](../reference/authentication-config.md).

## Authentication is off by default

A fresh Flare instance has **no login requirement at all** — anyone who can
reach the dashboard sees the Logs page immediately, with full access.
There's no forced setup step blocking you before you can look at anything.
This is a deliberate default for a self-hosted, often-single-user or
internal-network tool: requiring an account before you've even decided you
want one is friction nobody asked for.

**Upgrading an existing deployment stays protected.** The opt-in default
only applies to a genuinely *fresh* database with zero users in it. If
you're upgrading a Flare instance that already has accounts, the migration
that introduces this switch seeds it as `Enabled = true` for you — auth
stays required exactly as it already was; nothing opens up silently on
upgrade.

### How this is enforced

A single global flag, `AuthSettings.Enabled`, is read by
`ConditionalAuthorizationMiddlewareResultHandler` (`Flare.Api/Auth/`) — a
thin wrapper around ASP.NET Core's own
`AuthorizationMiddlewareResultHandler`. When the flag is `false`, it
short-circuits *every* authorization check in the app (all of
`RequireAuthorization()`/`RequireMember`/`RequireAdmin` across every
endpoint group) to succeed unconditionally. When `true`, it delegates to
the framework's default handler exactly as before. This is the one choke
point that makes "off by default" possible without touching any of the
individual endpoint files.

Within that, **Local sign-in has its own enable flag** (`LocalEnabled`),
independent of the four method-specific ones — so all five methods (Local /
Entra / Active Directory / OpenID Connect / Reverse proxy) are turned on
and configured the same, symmetrical way. An org fully migrated to SSO can
eventually turn local password login off entirely, same as any other
method.

**Methods coexist, not exclusive.** All five can be enabled at once. This
is deliberate, not an accident of implementation: an exclusive
single-method design risks a real lockout — if an Entra/AD/OIDC/proxy
group→role mapping is misconfigured and nobody ends up `Admin`, there's no
way in — while coexistence keeps a local "break-glass" Admin path available
even when SSO/AD/proxy is the day-to-day method.

## The `/auth` page

Everything auth-related lives on one Admin-only page (reachable to anyone
while authentication is off, same as every other page):

![The /auth page](../screenshots/auth.png)

1. **Authentication** — the umbrella "Require sign-in" switch and the
   `Local username/password` toggle sit together at the top. Off → an
   explanatory blurb, everything below is inert. On → the four method
   sections below become the live configuration surface.
2. **Microsoft Entra ID**, **Active Directory**, **OpenID Connect**, and
   **Reverse proxy** — each section's own enable toggle plus inline
   configuration form. Settings save independently per section.
3. **Users** — every account (any provider), with role and enable/disable
   controls, shown once any method is on.

`/setup`, `/security`, and `/users` no longer exist as separate routes;
`/auth` replaces all three.

## How sessions work

Logging in sets an httpOnly session cookie (`flare_session` by default) —
not a JWT. The cookie's value is an opaque, server-side-tracked token;
deleting the corresponding row (logout, or an admin disabling the account)
revokes it immediately, which a self-contained JWT can't do without a
denylist. This also means the live-tail WebSocket "just works" once you're
logged in: the browser sends the same cookie on the WebSocket upgrade
request as any other same-site request, no separate token needed.

Sessions default to a 14-day fixed expiry (`Auth:SessionLifetime`), no
sliding window.

## Where accounts live

Users, sessions, ingest API keys, and all auth settings are stored in an
**embedded SQLite file**, not a separate database container — why, and the
trade-off it creates (`Flare.Api` limited to a single replica), is recorded
in
[ADR-0004](../../docs-internal/adr/0004-embedded-sqlite-for-identity.md).
`Flare.Ingest` shares the same file, read-mostly.

## Ingest API keys

Separate from user accounts on purpose: an ingest API key authenticates a
*machine* (an app's OTLP exporter), not a *person*. Tying it to a `Users`
row would force every telemetry-emitting app to be linked to someone's
login, which doesn't match how collectors/exporters are actually operated
(one or a few shared keys per environment). See
[the how-to guide](../how-to/configure-authentication.md#ingest-api-keys)
for creating/using/revoking one.

## How each method works

All five methods end in the same kind of session — `flare_session`,
`RequireMember`/`RequireAdmin`, every other endpoint — regardless of which
door you came in through. **First-ever login for a given external identity
creates a `Users` row; every login after that signs in as the existing
row, with its role unchanged** — role changes after that live in Flare
(the Users table on `/auth`), not in the upstream provider, for every
non-local method.

### Microsoft Entra ID (SSO)

Wired through ASP.NET Core's standard multi-scheme `AddAuthentication()`
model: the cookie scheme (`SessionAuthenticationHandler`, registered as
`FlareSession`) stays the *only* scheme any endpoint's
`RequireAuthorization()` ever actually resolves a principal through. Entra
ID is a second, separate front door that ends in exactly the same kind of
session — no existing endpoint needed to change to support it.

**Single-tenant only** — Flare validates against one specific Entra
directory, not `common`/`organizations`. **Configured per-instance,
through the dashboard, not config files** — there's no
`Auth:Entra:TenantId`/`ClientId`/`ClientSecret` in config/`.env`/
docker-compose; the database is the only place these live (see
[ADR-0004](../../docs-internal/adr/0004-embedded-sqlite-for-identity.md)).
Settings changes take effect after restarting `Flare.Api` — not live, by
design (see `EntraOpenIdConnectOptionsConfigurator`'s remarks in
`Flare.Api/Auth/` for the simplicity/risk trade-off).

Flow: the dashboard's "Sign in with Microsoft" button navigates to
`GET /api/auth/entra/login`, which challenges the `Entra` OpenIdConnect
scheme. Microsoft redirects back to `/signin-oidc`, which hands off to
`GET /api/auth/entra/complete`. That endpoint looks up the account by the
token's `oid` claim (`Users.AuthProvider = 'Entra'`,
`Users.ExternalId = <oid>`).

**Role source: Entra App Roles**, matched by name against Flare's own
`Admin`/`Member`/`Viewer` enum — Flare reads the token's `roles` claim on
first sign-in and picks the highest-privilege match. No App Role assigned
provisions `Auth:Entra:DefaultRole` instead (`Viewer` unless overridden) —
the one Entra-related setting that's still config-bound rather than
dashboard-configured. A disabled account bounces to
`/login?error=account-disabled` instead of getting a session.

### Active Directory (LDAP)

Sign in against an existing Active Directory (or AD-compatible — e.g.
Samba AD, or a generic LDAP directory laid out similarly) domain, without
Flare or its container being domain-joined. This uses **LDAP/LDAPS bind
from Flare's own login form** — not Windows Integrated Auth/Kerberos SSO,
which would require the `Flare.Api` container itself to be domain-joined.
A network-reachable LDAP/LDAPS endpoint on your domain controller is all
this needs.

**No restart required, unlike Entra ID** — LDAP auth registers no ASP.NET
Core authentication *scheme*; each login attempt reads current settings
fresh from SQLite and opens a plain LDAP connection imperatively.

Flow: `POST /api/auth/ldap/login` first binds as the configured **service
account**. A bind/connection failure here returns **`502`**, distinct from
a wrong-password `401`, so a broken Flare-side LDAP config isn't mistaken
for "everyone's password is suddenly wrong." Still bound as the service
account, Flare searches `BaseDn` using `UserSearchFilter` with the
submitted username substituted in (escaped per RFC 4515 before
interpolation — the LDAP-injection equivalent of this repo's parameterized
SQL/ClickHouse queries elsewhere). No match → a generic `401` (Flare
deliberately doesn't distinguish "no such user" from "wrong password,"
same anti-enumeration stance as local login). Flare then **re-binds as the
found user's DN** with the password actually submitted, on a fresh
connection — this, not the service-account bind, is what verifies the
password.

**Role source: three configurable group DNs** (Admin/Member/Viewer),
checked against the directory's `memberOf` attribute — highest-privilege
match wins. **Nested group membership resolves against real Microsoft AD**
via `LDAP_MATCHING_RULE_IN_CHAIN` (OID `1.2.840.113556.1.4.1941`) — an
AD-specific extension; against a non-AD directory that doesn't understand
it, each such search simply fails and is treated as "not a nested member"
(direct membership still resolves normally there).

**Known limitations, stated plainly:**
- Nested group membership only resolves against real Microsoft AD.
- Built and named for Microsoft AD/AD-compatible directories — though the
  Advanced overrides (search filter, unique ID attribute) are flexible
  enough to point this at a plain OpenLDAP directory too, as this
  feature's own verification testing did.
- **Linux containers need the native LDAP client library installed.**
  `System.DirectoryServices.Protocols` (the .NET LDAP client Flare uses)
  is a P/Invoke wrapper over the OS's own OpenLDAP client on Linux, not a
  pure-managed implementation — Flare's own `Flare.Api` Docker image
  already installs `libldap2`, but a custom/non-Docker deployment on Linux
  needs it present too, or every LDAP login attempt fails with an
  unhandled error instead of a clean 401/502.

### OpenID Connect

Sign in against any standards-compliant OpenID Connect provider — Okta,
Auth0, Keycloak, Authentik, and the like — not just Microsoft Entra ID.
Architecturally a close cousin of Entra ID: Entra is already just a named
`AddOpenIdConnect()` scheme with a hardcoded Microsoft authority URL
pattern; the generic `Oidc` scheme is a second, independent
`AddOpenIdConnect()` registration that applies `Authority` as-is instead
of interpolating a tenant id — everything else (paired short-lived
external cookie, database-backed options, restart-required semantics) is
the same mechanism.

**v1 is sign-in only** — unlike some providers' dashboards (Seq's, for
instance), Flare doesn't yet propagate logout to the provider's own
end-session endpoint; `/api/auth/logout` just clears the local session
cookie for OIDC-provisioned accounts, same as every other method today.

Flow: the account is looked up by the token's `sub` claim — the portable,
standard OIDC identifier (`Users.AuthProvider = 'Oidc'`), unlike Entra's
Microsoft-specific `oid` preference.

**Role source: a dashboard-configurable claim name** (default `roles`) —
arbitrary OIDC providers vary widely in what claim (if any) carries
role/group information, so unlike Entra's fixed claim, the name itself is
configurable (Okta's default is `groups`, for instance). No recognized
value provisions **Default role** instead — here, unlike Entra's
`Auth:Entra:DefaultRole`, a dashboard-configured field, not config-bound.

### Reverse proxy (trusted header)

Trust an identity header a reverse proxy sitting in front of Flare already
sets, once *it's* authenticated the request — Authelia, Authentik,
oauth2-proxy, Cloudflare Access, Tailscale Serve, or anything else that
terminates its own login flow and forwards requests on. Flare never talks
to an IdP itself for this method; it just reads a header.

**The lightest-weight method, and the one with the sharpest edge.** A
header is nothing more than text on an HTTP request — any client that can
reach `Flare.Api` directly (not through the trusted proxy) can set it to
anything, including `Admin`'s own username. Every design decision below
exists to make that mistake hard to make by accident:

- **A trusted-network allowlist is mandatory, not optional.** Enabling
  this method without at least one valid CIDR configured is refused
  server-side (`400`) — there is no "trust everyone" default. The header
  is only honored when the *caller's own* TCP connection
  (`HttpContext.Connection.RemoteIpAddress`, not a forwarded/spoofable
  header) falls inside a configured range.
- **No `X-Forwarded-For` trust chain.** Flare deliberately does not call
  `UseForwardedHeaders()` or read `X-Forwarded-For` to determine the "real"
  client — trusting one spoofable header to establish trust for a
  *different* spoofable header would defeat the entire point.
- **Corollary: `Flare.Api` must not be reachable except through the
  proxy.** If your compose/network setup exposes `Flare.Api`'s port
  directly, this method is not safe to enable.

**Dashboard-triggered, not ambient.** Unlike Entra/LDAP/OIDC, there's no
button — `/login` calls `POST /api/auth/proxy/login` automatically as soon
as it learns this method is enabled, since identity is already established
by the time the request arrives. A script or automation client calling
`Flare.Api` directly needs to hit that same endpoint once itself. **No
restart required** — like Active Directory, this registers no ASP.NET Core
authentication scheme.

**Role source: an optional second header**, split on commas and matched
against three configurable group names — the same shape Active Directory's
three group DNs establish, just header values instead of directory
attributes. No match (or no groups header configured) → Default role.

**Known limitations, stated plainly:**
- **Logout doesn't propagate to the proxy/IdP automatically.** As long as
  the proxy keeps sending the header, `/login` silently re-establishes a
  session right after a manual logout. A manual escape hatch exists: an
  optional **Logout redirect URL**, sent to instead of `/login` right
  after clearing Flare's own session.
- **A misconfigured or over-broad trusted-CIDR range is a real security
  hole, not a theoretical one.** Flare rejects the one most catastrophic
  case outright (`0.0.0.0/0`/`::/0` can't be saved), but anything short of
  that is still the operator's call to get right.