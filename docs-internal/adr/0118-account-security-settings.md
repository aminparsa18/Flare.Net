# ADR-0118: Account & security settings

Status: accepted

## Context

`/settings/account` only offered a password change for local accounts, and there was no way to see or
revoke login sessions or to take your own saved views and dashboards out of Flare.

## Decision

`/settings/account` becomes "Account & security" and is shown to every signed-in user; the password card
stays local-only. It gains:

- **Active sessions.** `GET /api/auth/sessions`, `DELETE /api/auth/sessions/{handle}` and
  `DELETE /api/auth/sessions[?keepCurrent=true]`, self-service on the plain authenticated route group.
  `ISessionStore.ListForUserAsync` is new; no migration. A session is addressed by a one-way handle
  (first 16 bytes of SHA-256 of the token, hex), never the token, because the token is the cookie value.
  Revoke resolves the handle only among the caller's own sessions.
- **Data export.** Client-side: the page fetches `/api/views` and `/api/dashboards` and downloads one
  JSON file. Saved views are workspace-shared and have no owner, so all visible views are included;
  dashboards are filtered to `ownerUserId` equal to the caller (ADR-0027).

No profile editing: accounts have a username and role only, so there is nothing to edit. Personal access
tokens keep their own page.

## Consequences

Sessions show created / last active / expiry but not device or IP, since the table does not store them.
A PAT-authenticated caller has no current session, so "keep current" keeps nothing.
