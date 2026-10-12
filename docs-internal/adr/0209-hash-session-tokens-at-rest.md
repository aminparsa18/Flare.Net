# ADR-0209: Hash session tokens at rest

Status: Accepted

Date: 2026-10-12

## Context

`Sessions.Id` held the raw cookie token, while personal access tokens and ingest keys were already stored as SHA-256
digests. Anyone who could read the identity database (a backup, a Postgres replica, a SQL injection) could replay a row
as a live login.

## Decision

- **`Sessions.Id` is the hex SHA-256 of the cookie token** (`SessionTokenHasher`). Every lookup, touch and delete hashes the
  presented token first. The input is 256 bits of CSPRNG output, so a fast hash is enough, as for PATs and ingest keys.
- **No schema change; migration `0036` (SQLite) / `0011` (Postgres) deletes existing rows.** Old rows hold raw tokens that
  no longer match any digest, so they are removed rather than left as live credentials. Every user signs in once after the
  upgrade.
- **`Session.Token` carries the raw value only on the instance `CreateAsync` returns**, so the sign-in endpoints can set the
  cookie. Found and listed sessions have `Token = null`.
- **`ISessionStore.DeleteByIdAsync`** revokes from a listed session (the self-service revoke endpoint only has the digest).
  The session "handle" shown in the API is now the first 32 hex characters of the stored digest.

## Consequences

- A leaked database no longer yields usable session cookies.
- The cookie value, the API surface and the wire format are unchanged.
