# ADR-0082: Service accounts as a user kind, issued tokens by Admins

Status: Accepted

Date: 2026-10-03

## Context

[ADR-0019](0019-personal-access-tokens.md) made personal access tokens the
credential for scripts, CI and the query API, but a token authenticates as the
person who made it. CI, a Grafana datasource or the `/mcp` endpoint therefore
borrows a human account, and stops working when that person is disabled or
leaves. Admins also can't tell from the audit log which pipeline did something.

## Decision

**A service account is a `Users` row with `AuthProvider = 'ServiceAccount'`**
(`Migrations/0021_service_accounts.sql` widens the CHECK constraint with the
usual table rebuild). It has a name, a role and a disabled flag, and nothing
else.

- Authentication is unchanged: the existing handler resolves a PAT to a user,
  rejects disabled users, and applies the user's role and the per-token rate
  limit ([ADR-0028](0028-personal-access-token-rate-limiting.md)). Roles, `/api/users` listing and
  the role/disable endpoints, and the dashboard's Users table all work for
  service accounts with no new code.
- It can never log in: it gets a hash of a random, discarded password (the same
  device `CreateFromExternalAsync` uses), and every SSO lookup is keyed by a
  different provider.
- **Only an Admin issues its tokens**: `POST /api/service-accounts`,
  `POST`/`GET /api/service-accounts/{id}/access-tokens`. Revoking reuses
  `DELETE /api/access-tokens/{id}` (Admins may revoke any token). The
  self-service `POST /api/access-tokens` returns 403 for a service-account
  principal, so a leaked token can't mint replacements or extend its own life.
- Audit: creation and token issue are classified as `service-account`
  `create`/`create-token` events; requests made with a service-account token are
  attributed to its name, like any other user.

## Alternatives considered

- **A separate `ServiceAccounts` table and principal type.** Rejected: every
  place that reads a user (auth handler, rate limiting, audit, role policies)
  would need a second branch for no behavioral difference.
- **Letting service accounts self-issue tokens.** Rejected: rotation and issue
  should be an Admin act that the audit log can name.

## Consequences

- `AuthProvider` gains a value, so any code that switches over providers must
  handle `ServiceAccount` (the dashboard's `AuthProvider` type does).
- A disabled service account's tokens stop working immediately; re-enabling
  restores them.
