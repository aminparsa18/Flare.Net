# ADR-0116: Shared forgot-password throttle

Status: accepted

## Context

ADR-0113 throttled forgot-password with an in-memory per-username cooldown, which resets on restart
and isn't shared across API replicas.

## Decision

The throttle moves into the identity database. `IPasswordSetTokenStore.TryCreateAsync` deletes the
user's tokens older than the interval and inserts a new one only if none remain, i.e. none was issued
within the last minute (any purpose). The forgot-password endpoint sends mail only when it returns a
token. No migration: `PasswordSetTokens.CreatedAt` already exists. This supersedes the in-memory
cooldown in ADR-0113; the rest of that ADR stands.

## Consequences

Works across replicas and restarts on both SQLite and Postgres. A recent admin invite/reset token also
delays a self-service request for up to a minute. Unknown usernames are no longer counted, which is fine
since they trigger no mail. Under Postgres read-committed, two truly simultaneous requests could both
insert; the worst case is two emails.
