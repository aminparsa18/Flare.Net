# ADR-0112: Local user invites and single-use set-password tokens

Status: accepted

## Context

Local auth could bootstrap one admin but offered no way to add another local
user, reset a forgotten password, or change your own. Live runs wrote users into
the identity database by hand.

## Decision

- A `PasswordSetTokens` table (SQLite `0027`, Postgres `0002`) holds the SHA-256
  of a 256-bit random token, the user, a purpose (`Invite`/`Reset`) and an expiry.
  One live token per user; redemption is a single `DELETE ... RETURNING`, so a
  token works once even under concurrent use.
- Admins `POST /api/users/invite` (creates a local user with a random, discarded
  password) or `POST /api/users/{id}/password-reset`, and get the raw token back
  once. The dashboard turns it into a `/set-password?token=` link. Invites last
  3 days, resets 24 hours.
- `POST /api/auth/set-password` (unauthenticated; the token is the credential)
  sets the password and revokes all of the user's sessions. A too-short password
  is rejected before the token is consumed.
- `POST /api/auth/password` verifies the current password, sets the new one and
  revokes the user's other sessions (`ISessionStore.DeleteAllForUserExceptAsync`).
- Admin reset also revokes sessions immediately.
- Links are not emailed. Emailing needs SMTP configuration in `Flare.Api` and a
  public-URL setting; deferred (see roadmap).

## Consequences

An admin sees the token once and must hand it over securely. SSO accounts have no
Flare-managed password and are rejected by all of these endpoints.
