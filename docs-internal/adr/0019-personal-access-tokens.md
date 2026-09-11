# ADR-0019: Personal access tokens as a query-API bearer credential, folded into the existing session scheme

Status: Accepted
Date: 2026-09-11

## Context

Every `/api/logs`, `/api/alerts`, etc. endpoint in `Flare.Api` sits behind
`SessionAuthenticationDefaults` (the `flare_session` cookie) or one of the
SSO schemes (Entra/OIDC/LDAP/reverse-proxy — all of which, in the end,
also just mint a `flare_session` cookie via `SessionAuthenticationHandler`).
There was no bearer-token path for the query side at all, so a
script/CI job/another service had no way to call Flare's API without
impersonating a browser session. `IngestApiKeyEndpoints.cs`'s own doc
comment already flagged the gap: ingest API keys authenticate
telemetry-emitting machines calling `Flare.Ingest`'s OTLP receiver, "not
dashboard users." SigNoz added Personal Access Tokens for exactly this
([signoz#2261](https://github.com/SigNoz/signoz/commit/b99d7009a)) — a
durable, user-scoped bearer token for programmatic API access, separate
from machine-scoped ingest keys. See former
`docs-internal/planning/roadmap.md` entry "No API tokens for the query
API."

## Decision

**A personal access token (PAT) authenticates as the `User` who created
it, with exactly that user's own role/permissions** — not a new
permission concept, just a second credential the same identity can
present. Concretely:

- New `PersonalAccessTokens` table (`Migrations/0013_personal_access_tokens.sql`),
  keyed by a SHA-256 hash of a `flr_pat_`-prefixed 256-bit random token —
  the same hashing scheme as `IngestApiKeyHasher`, for the same reason
  (a high-entropy random secret checked per-request, not a login, has no
  brute-force surface PBKDF2 would meaningfully slow down). The prefix
  exists only so a person/secret-scanner can recognize the token at a
  glance (same idea as GitHub's `ghp_`); ingest keys have no such prefix
  because nobody eyeballs those.
- **`SessionAuthenticationHandler` (the one scheme already registered as
  Flare.Api's default) resolves EITHER the `flare_session` cookie OR an
  `Authorization: Bearer flr_pat_...` header**, not a second
  `AddAuthentication().AddScheme()` registration plus a policy scheme to
  pick between them. Every existing `RequireAuthorization()`/
  `RequireMember`/`RequireAdmin` group in `Program.cs` therefore accepts
  a PAT with zero changes — the same "wrap once, nothing downstream
  changes" property the SSO schemes' cookie-issuing precedent already
  established.
- Self-service, not admin-issued: `POST /api/access-tokens` sits behind
  the plain `RequireAuthorization()` group (any authenticated
  Viewer-and-up), unlike ingest API keys (`RequireAdmin` — a leaked
  ingest key lets anyone ingest telemetry as the whole instance; a
  leaked PAT only ever carries its own creator's existing permissions,
  so self-issuance adds no new privilege).
- Optional expiry (`ExpiresInDays`, 1–365, or omitted for "never
  expires") — matches ingest keys' own no-expiry precedent as the
  default, but gives a caller GitHub-style bounded-lifetime tokens when
  they want one.

## Alternatives considered

- **A dedicated `PersonalAccessTokenAuthenticationHandler` registered as
  its own scheme**, selected via `AddPolicyScheme()`'s forwarding
  selector (inspect the request, forward to session-cookie or PAT
  scheme). Rejected: doubles the moving parts (two handlers, one
  selector) to solve a problem the existing handler already solves by
  just checking one more place a credential could be — a single
  `HandleAuthenticateAsync()` that tries the header before the cookie is
  strictly simpler and keeps one scheme name for every downstream
  authorization check to reason about, exactly as this handler's own
  pre-existing remarks already anticipated for a *future OIDC/Entra*
  scheme (that one still gets its own registration, because it's a
  genuinely different protocol exchange — a PAT isn't; it's just another
  bearer credential for the same claims shape this handler already
  produces).
- **Admin-issued tokens scoped to another user** (mirroring ingest keys'
  admin-only issuance). Rejected: SigNoz's own PAT feature, and the
  roadmap item itself, frame this as *user-scoped* self-service — a
  token that stands in for "this user, from a script" rather than a
  separate machine identity. Admin oversight (list/revoke *other*
  users' tokens) is deliberately left out of this pass; see
  `PersonalAccessTokenEndpoints`'s own remarks for the fallback (an
  Admin can still disable the owning account, or revoke one specific
  token by id) and what would need to change to add it properly.
- **A `Flare.Cli` `token create` command**, mirroring `apikey create`'s
  precedent. Rejected: `flare apikey create` works unauthenticated
  against `localhost` because ingest keys aren't tied to a user — there
  is no such thing as "create a PAT for no one in particular." The CLI
  has no logged-in identity to mint a PAT *as*, so this doesn't have a
  CLI-shaped answer the way ingest keys did. The dashboard's terminal
  modal (`terminal/commands/token.ts`) can still do it, because it runs
  inside an actual authenticated browser session and just rides that
  session's own cookie the same way every other terminal command
  already does — only the standalone CLI process lacks an identity to
  attach a token to.

## Consequences

- A script/CI job/another service authenticates exactly like
  `curl -H "Authorization: Bearer flr_pat_..." https://.../api/logs/search`
  — no session cookie, no browser, no impersonation needed.
- PATs do NOT work against the live-tail WebSocket upgrade
  (`LogTailEndpoints`) — a browser can't attach a custom header to that
  handshake the way it automatically sends the cookie. This was already
  true in spirit (nothing "logs in" a script to a live WebSocket
  session) and is called out directly in
  `SessionAuthenticationHandler`'s doc comment rather than left as a
  surprise.
- Disabling a `User` account (`IsDisabled`) instantly invalidates every
  PAT that user issued, the same way it already invalidates every
  session — both paths re-resolve the owning `User` on every request
  and reject a disabled one. No separate "revoke all this user's
  tokens" sweep was needed for that case.
- `Flare.Cli` gets no equivalent `token` command (see above) — this is a
  deliberate scope boundary, not an oversight; revisit only if the CLI
  ever grows its own login/identity story.

## Related documentation

- `docs/reference/authentication-config.md` — the config-facing summary
- `docs/explanation/authentication-model.md` — the "why" per auth method
- ADR-0004 — why identity is embedded SQLite (the store this table lives in)
