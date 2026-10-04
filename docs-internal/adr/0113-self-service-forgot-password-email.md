# ADR-0113: Self-service forgot-password by email

Status: accepted

## Context

ADR-0112 let admins hand out set-password links but left a locked-out user waiting
on an admin. Flare already has an app-wide SMTP server (`Email:*`, used by email
alert channels) and a public dashboard URL (`Alerting:PublicUrl`). Users have no
email column; local usernames are free text.

## Decision

- `POST /api/auth/forgot-password` (unauthenticated, `{ username }`) issues a
  1-hour `Reset` token (same store and redemption path as ADR-0112) and emails
  `{PublicUrl}/set-password?token=...`.
- The username *is* the address: a link is sent only when a live local account's
  username parses as an email. No schema change; accounts with non-email usernames
  keep relying on the admin reset link.
- The endpoint returns 404 unless local login is on and SMTP (`Email:Host`,
  `Email:From`) plus `Alerting:PublicUrl` are configured. Otherwise it always
  returns 204, so it can't be used to learn which usernames exist.
- A 1-minute per-username cooldown (in-memory, per API replica) limits mail-bombing.
  It applies whether or not the account exists.
- `GET /api/auth/bootstrap/status` gains `passwordResetEmailEnabled`; the login page
  shows "Forgot password?" only when it is true.
- Redeeming the link revokes all sessions, as in ADR-0112.

## Consequences

Not durable across restarts or shared across replicas, so the cooldown is a
speed bump, not a guarantee. Emailing invites and bulk invite are still open.
