# How to configure authentication

Turn on sign-in and set up one or more of Flare's five auth methods. For
what each method actually does and why it's built this way, see
[`../explanation/authentication-model.md`](../explanation/authentication-model.md);
for exact config keys and the roles table, see
[`../reference/authentication-config.md`](../reference/authentication-config.md).

Everything below happens on one Admin-only page, **`/auth`**:

![The /auth page](../screenshots/auth.png)

## Turn on sign-in

1. Open `/auth` (reachable to anyone while authentication is off, same as
   every other page while it's off).
2. Flip the top-level **"Require sign-in"** switch on. This reveals five
   method sections — Local / Microsoft Entra ID / Active Directory /
   OpenID Connect / Reverse proxy — each with its own enable toggle and
   inline configuration, plus a Users table underneath.
3. Create your first **Local** account, in the Local section. The first
   Local account you create is always `Admin` — there's no separate
   "setup mode" beyond that; once at least one Local account exists, sign-in
   works the normal way. Do this even if your real plan is SSO/AD/proxy —
   it's your break-glass path if a group→role mapping ever gets
   misconfigured.

You can enable any combination of the five methods at once — they coexist,
not exclusive, by design.

## Set up Microsoft Entra ID (SSO)

1. In Entra ID → **App registrations → New registration**. Single tenant
   ("Accounts in this organizational directory only").
2. **Redirect URI**, platform "Web": sign in to Flare with your local Admin
   account and open `/auth` — it displays the *exact* redirect URI to
   paste here, computed from whatever host/port you're actually reaching
   `Flare.Api` on. For a local `docker compose up` this is
   `http://localhost:8080/signin-oidc` by default; use `https://` once
   reachable beyond your own machine (see the HTTPS caveat below).
3. **Certificates & secrets → New client secret.** Copy the value
   immediately — like an ingest API key's raw value, Azure only shows it
   once.
4. **App roles**: add three `appRoles` entries with `"value"` set to
   exactly `Admin`, `Member`, and `Viewer` (case-insensitive match, but
   keep them exact) and `"allowedMemberTypes": ["User"]`. Then, in
   **Enterprise applications → your app → Users and groups**, assign each
   person (or group) the App Role that should become their Flare role. No
   App Role assigned (or none configured at all) provisions
   `Auth:Entra:DefaultRole` instead (`Viewer` unless overridden).
5. Note the **Application (client) ID** and **Directory (tenant) ID** from
   the registration's Overview page.
6. Back in Flare's `/auth` page, Microsoft Entra ID section: paste the
   Directory (tenant) ID, Application (client) ID, and client secret, flip
   **Enabled** on, and **Save**.
7. **Restart `Flare.Api`** (`docker compose restart api`, or an `aspire
   resource restart` in local dev) — Entra settings changes take effect on
   restart, not live.
8. Sign out and confirm "Sign in with Microsoft" appears on the login
   page; sign in with a real account to confirm end-to-end.

**HTTPS caveat:** the correlation cookies ASP.NET Core's OIDC handler sets
during the redirect round-trip to Microsoft need to survive a cross-site
navigation, which browsers only reliably allow over HTTPS (Chrome/Edge
special-case plain `http://localhost`, which is why local dev works). Any
real deployment reachable beyond your own machine needs HTTPS for Entra ID
sign-in to work.

## Set up Active Directory (LDAP)

1. On `/auth`, in the Active Directory section, fill in:
   - **Host** / **Port** — your domain controller's address. Defaults to
     port 636 (LDAPS).
   - **Use LDAPS (TLS)** — on by default; leave it on for anything beyond
     a throwaway test directory.
   - **Pinned server certificate** (optional) — paste a PEM-encoded
     certificate to pin TLS trust for this connection to exactly that
     certificate, bypassing the OS trust store. Use your internal CA's
     root certificate if the DC's certificate is signed by a private CA,
     or the DC's own certificate if self-signed. You can paste more than
     one certificate back to back — a root plus the intermediate that
     actually signed the DC's leaf certificate, or two DC certificates
     side by side to ride out a rotation window — same concatenated-PEM
     convention as any CA bundle file; at least one certificate in the
     bundle must be self-signed to serve as a trust anchor. Get it with
     `openssl s_client -connect dc.corp.example.com:636 -showcerts`, whose
     output already includes the full chain. Leave blank to keep relying
     on the OS/container trust store (the default).
   - **Base DN** — the search root, e.g. `DC=corp,DC=example,DC=com`.
   - **Bind DN (service account)** / **Bind password** — a directory
     account with read access to search for users under the Base DN.
     Doesn't need to be an Admin account in AD itself.
   - **Admin / Member / Viewer group DN** (optional) and **Default role** —
     three group DNs; a user's `memberOf` (including nested membership on
     real Microsoft AD) is checked against each, highest-privilege match
     wins, no match falls to Default role.
   - **Advanced** (collapsed by default) — override **User search filter**
     (default `(&(objectClass=user)(sAMAccountName={0}))`, AD's own
     convention) or **Unique ID attribute** (default `objectGUID`) only if
     pointing this at something other than a standard AD layout, e.g.
     OpenLDAP's `(&(objectClass=inetOrgPerson)(uid={0}))` filter with
     `entryUUID` as the unique ID attribute.
2. Flip **Enabled** on and **Save** — takes effect immediately, no restart.
   The bind password is never echoed back once saved — leave that field
   blank on a later edit to keep the currently-saved value.
3. Sign out and confirm the "Active Directory" option now appears next to
   "Local" on the login page; sign in with a real directory account to
   confirm end-to-end.

**Known limitations, stated plainly:**
- Nested group membership only resolves against real Microsoft AD, not
  other directories.
- Built and named for Microsoft AD/AD-compatible directories — the
  Advanced overrides are flexible enough to point this at a plain OpenLDAP
  directory too, as this feature's own verification testing did.
- **Linux containers need the native LDAP client library installed**
  (`libldap2` — already in Flare's own `Flare.Api` Docker image, but a
  custom/non-Docker Linux deployment needs it too).

## Set up OpenID Connect

Use this for any standards-compliant OIDC provider that isn't Microsoft
Entra ID — Okta, Auth0, Keycloak, Authentik, and the like.

1. Register Flare as an application/client with your OIDC provider. Every
   provider needs:
   - **Redirect/callback URI** — sign in to Flare with your local Admin
     account and open `/auth`; the OpenID Connect section displays the
     *exact* callback URL to paste in.
   - A **client secret** — copy the value immediately; most providers only
     show it once.
2. Back in Flare's `/auth` page, OpenID Connect section, fill in:
   - **Display name** — shown on the sign-in page as "Sign in with
     {Display name}".
   - **Authority** — your provider's issuer URL, e.g.
     `https://your-tenant.okta.com`. Flare fetches
     `{Authority}/.well-known/openid-configuration` for the rest.
   - **Client ID** / **Client secret**.
   - **Scopes** — defaults to `openid profile email`.
   - **Role claim name** (default `roles`) — set to whatever your provider
     actually issues; Okta's default is `groups`, for instance.
   - **Default role** — used when the claim is absent or has no
     recognized value.
3. Flip **Enabled** on and **Save**, then **restart `Flare.Api`**
   (`docker compose restart api`, or `aspire resource restart` in local
   dev) — takes effect on restart, not live.
4. Sign out and confirm "Sign in with {Display name}" appears on the login
   page; sign in with a real account at your provider to confirm
   end-to-end.

**HTTPS caveat:** same as Entra ID — the correlation cookies need to
survive a cross-site navigation, which requires HTTPS beyond
`http://localhost`.

**Known limitation:** v1 is sign-in only — Flare doesn't yet propagate
logout to the provider's own end-session endpoint; `/api/auth/logout`
just clears the local session.

## Set up reverse-proxy (trusted header) auth

Use this if a reverse proxy already sitting in front of Flare —
Authelia, Authentik, oauth2-proxy, Cloudflare Access, Tailscale Serve, or
similar — has already authenticated the request and can forward the
identity as a header.

**Before you start**, make sure `Flare.Api` is not reachable except
through that proxy — if your compose/network setup exposes `Flare.Api`'s
port directly, this method is not safe to enable (see
[the explanation](../explanation/authentication-model.md#reverse-proxy-trusted-header)
for why).

1. Configure your reverse proxy to authenticate requests and forward the
   signed-in identity as a header — the exact steps vary by proxy.
   Authelia/oauth2-proxy-style setups typically call this `Remote-User`
   (Flare's own default) or `X-Forwarded-User`; check your proxy's own
   docs for the exact header name and whether it also emits a groups
   header.
2. Find your Docker network's actual subnet
   (`docker network inspect <network>` — don't guess `172.18.0.0/16`,
   confirm it) or the specific address(es) your proxy connects from.
3. On `/auth`, in the Reverse proxy section, fill in **Header name**
   (matching whatever your proxy actually sends) and **Trusted proxy
   CIDRs** (one per line — required; a valid CIDR is mandatory, there is
   no "trust everyone" default). Set **Groups header name** / group
   fields / **Default role** if you want group-based role mapping.
   Optionally, under **Advanced: logout**, set **Logout redirect URL** to
   your proxy's (or IdP's) own sign-out URL — see "Known limitations"
   below for why this is a manual step.
4. Flip **Enabled** on and **Save** — takes effect immediately, no
   restart.
5. Reach Flare **through the proxy** (not directly) and confirm `/login`
   signs you in silently. Then confirm hitting `Flare.Api` directly
   (bypassing the proxy, if that's even reachable in your setup) with a
   hand-set header gets a `403`, not a session.

Direct API/script callers (never having loaded the dashboard) need to call
`POST /api/auth/proxy/login` once themselves through the proxy to obtain a
session — this method doesn't authenticate every request ambiently.

**Known limitations, stated plainly:**
- **Logout doesn't propagate to the proxy/IdP automatically** — as long as
  the proxy keeps sending the header, `/login` silently re-establishes a
  session right after a manual logout. Set **Logout redirect URL**
  (Advanced: logout) as the manual escape hatch — Flare's "Log out" then
  sends the browser there instead of back to `/login`.
- **A misconfigured or over-broad trusted-CIDR range is a real security
  hole, not a theoretical one.** Flare rejects the one most catastrophic
  case outright (`0.0.0.0/0`/`::/0` can't be saved), but anything short of
  that — e.g. an entire `10.0.0.0/8` when only one proxy container needed
  trusting — is still your call to get right; there's no way for Flare to
  know what your actual network topology should be.

## Ingest API keys

Ingest API keys authenticate OTLP exporters (machines), separate from user
accounts.

- **Create one**: `Admin`-only, `POST /api/ingest-keys` (name it something
  like `"prod-collector"`). The raw key is shown **exactly once** — copy
  it somewhere safe immediately.
- **Use it**: send `Authorization: Bearer <key>` on your OTLP exporter
  (gRPC or HTTP — both checked the same way).
- **Revoke it**: `DELETE /api/ingest-keys/{id}`. Takes effect within 30
  seconds — `Flare.Ingest` caches the active-key set in memory and
  refreshes it on a timer rather than hitting SQLite on every ingest
  request.
- **Turn on enforcement** once you've migrated every exporter: create at
  least one key, update your exporters to send it, *then* set
  `Auth:IngestKeyRequired=true` (defaults to `false`, so upgrading an
  existing deployment doesn't suddenly reject anonymous ingest). This flag
  is independent of the dashboard's "Require sign-in" switch — ingest
  enforcement and dashboard/API-user auth are separate gates.
- **For automated setups** (an AppHost wiring a resource graph before
  either process has started, where "click a button in the dashboard"
  doesn't fit): `Auth:StaticIngestApiKey` is a fixed key set via
  configuration, valid alongside any keys created through the UI. This is
  what `Flare.AppHost` (local dev) and `Aspire.Hosting.Flare`'s
  `AddFlare(..., apiKey: ...)` use.

## Managing users

`Admin`-only, in the Users section of `/auth` (`GET`/`PATCH /api/users/*`) —
list every account (any provider), change a role, or enable/disable an
account. This is also where you promote a newly-auto-provisioned Entra/AD/
OIDC/reverse-proxy account past its initial role, or disable one without
waiting for someone to remove their group/role assignment upstream. Flare
refuses to demote or disable the **last enabled Admin** — that would be a
lockout recoverable only by editing the SQLite file directly.

## Backups

The identity SQLite file isn't backed up by any special tooling — include
the `identity-data` volume (docker-compose) or `.data/identity/` directory
(local Aspire dev) in whatever backup story you already have for the
`clickhouse-data`/`redis-data` volumes.