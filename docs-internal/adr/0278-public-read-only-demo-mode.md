# ADR-0278: Public read-only demo mode

Status: Accepted

Date: 2026-11-09

## Context

The public live demo linked from the README ran with auth off, Flare's default. With auth off
every request passes every authorization check
(`ConditionalAuthorizationMiddlewareResultHandler`), so every visitor was effectively an Admin.
Anyone could change auth settings, users, ingest keys, retention or alert channels.

Turning auth on and publishing a shared Viewer login is not enough:

- A Viewer can still change the shared account's password (`POST /api/auth/password`) and lock
  everyone else out, or sign out every session (`DELETE /api/auth/sessions`).
- A Viewer can save, rename and delete saved views, pin dashboards, write preferences, mint personal
  access tokens and call the AI endpoints.
- Visitors would first have to find and type the credentials.

A separate role (for example "DemoViewer") would add a fourth value to `UserRole`. That value is
persisted, `CHECK`-constrained and used as a claim in both identity providers, for a deployment
that exists once.

## Decision

A configuration-only switch, `Demo:Enabled` (`Demo__Enabled=true`), in `Flare.Api`. It is read
from configuration and never stored, so nothing reachable over HTTP can turn it off. The code is
in `src/Flare.Api/Demo/`.

1. **A shared demo account.** At startup `DemoProvisioner` makes sure a local account named by
   `Demo:Username` (default `demo`) exists, is enabled and is a Viewer. It sets a fresh random
   password on every start, so nobody can sign into it. It refuses to start if that username
   belongs to an Admin or a non-local account, rather than demoting or exposing it.
2. **Visitors don't log in.** `DemoMiddleware` runs between `UseAuthentication()` and
   `UseAuthorization()`. It gives any request without a valid session or token the demo
   account's principal, a Viewer with an extra `flare:demo` claim. Authorization then treats that
   principal like any other Viewer, so every Member/Admin route group answers 403 with no change
   to the route groups.
3. **Auth is always on.** `DemoAuthSettingsStore` wraps `IAuthSettingsStore` and always reports
   auth and local login as enabled, and saves them as enabled. An Admin can't open the instance
   to everyone, or lock themselves out of local login, from the `/auth` page.
4. **The demo account can't write anything.** For the `flare:demo` principal, `DemoMiddleware`
   refuses (403) every method other than GET/HEAD/OPTIONS unless the endpoint carries
   `AllowInDemoAttribute`. `Program.cs` puts the read-only endpoint groups on a `queryRoutes` group
   tagged `.AllowInDemo()`. Their POSTs are searches with structured bodies (`/api/logs/search`,
   Prometheus/Loki query endpoints, and so on). Login and logout are tagged too. Everything else
   stays untagged: saved views, pins, preferences, AI, personal access tokens, sessions, password
   change. A Viewer who signed in normally is not affected.
5. **One limit per visitor.** All visitors share one account, so the per-user limits that exist
   elsewhere don't tell them apart. A global rate limiter partitions demo-principal requests by
   client address (`Demo:RequestsPerMinute`, default 600). Behind a reverse proxy the deployment
   sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. That only works when the proxy is the only way
   in, as it is in `deploy/demo/`. Live tail's per-user socket cap is raised in the deployment for
   the same reason.
6. **An Admin must exist from the first start.** The demo account alone makes the user table
   non-empty. That closes first-run bootstrap (good: no visitor can claim the first Admin), but
   it also stops `Identity:Admin:*` from creating an Admin later. Startup therefore fails if
   demo mode is on without `Identity:Admin:Username`.

`GET /api/auth/me` returns `isDemo: true` for the demo principal. The dashboard uses it to show a
read-only banner, to offer **Sign in** instead of **Log out**, to let the demo visitor open
`/login`, and to hide the controls that write anything a Viewer may otherwise write. The UI changes
only hide controls; the API's 403 is what actually blocks the write.

The deployment is `deploy/demo/`: Docker Compose behind Caddy on one VM, with only ports 80 and
443 published. Its README has the server setup.

## Consequences

- Deny by default: a new non-GET endpoint that isn't on `queryRoutes` is blocked in the demo
  until someone tags it. Forgetting the tag breaks a read in the demo; it never opens a write.
  The opposite mistake is possible: an endpoint tagged `AllowInDemo` that writes. The tag goes on
  whole read-only `Map*` functions, and the existing split between read and write `Map*` functions
  (`MapErrorIssueReadEndpoints` vs `...WriteEndpoints`) is what keeps that safe.
- The MCP endpoint (`/mcp`, POST) is refused to the demo user. Its tools call back into the API
  with the caller's Bearer token, which the demo user doesn't have.
- If an Admin creates projects (ADR-0123), the demo account sees only projects it is a member of,
  like any Viewer. A demo with projects needs the demo account added to them.
- The login lockout keys on the visitor's address too, once forwarded headers are on. Without
  them, every visitor shares the proxy's address, and anyone could lock the Admin out by guessing
  wrong five times. A non-obvious Admin username also helps.
- Ingest, the alert worker and the CLI are unchanged. Demo mode is only a `Flare.Api` setting.
