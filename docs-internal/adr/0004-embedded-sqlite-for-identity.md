# ADR-0004: Embedded SQLite for identity/auth storage

Status: Accepted
Date: 2026-08-10 (introduced with local auth, v11)

## Context

Auth needed somewhere to persist users, sessions, ingest API keys, and every
auth method's settings (the global on/off switch, Entra config, LDAP config,
OpenID Connect config, reverse-proxy config). Flare already runs two backing
services — ClickHouse for log storage and Redis for the ingest buffer — and
adding a third database container for what amounts to a handful of small,
low-write tables needed to be weighed against the extra resource footprint
that imposes on every deployment, including the smallest single-user ones.

## Decision

Store identity/auth data in an **embedded SQLite file**, not a separate
database container. `Flare.Api` owns writes; `Flare.Ingest` shares the same
file read-mostly (checking ingest API key hashes) via `Identity__DbPath`.
The file lives on a local volume — `identity-data` in `docker-compose.yml`,
`.data/identity/` for local Aspire dev.

## Alternatives considered

- **A separate Postgres (or similar) container**, matching the shape of
  ClickHouse/Redis. Rejected for v1: a third backing service purely for a
  handful of small, low-write tables wasn't judged worth the added resource
  footprint on every deployment — including the common single-user,
  single-replica case where the trade-off this decision creates (below)
  never even matters.
- Precedent: **Seq** (a similar self-hosted, single-binary observability
  tool) keeps its own config/identity out of a separate database server
  too — used as a reference point that this is a reasonable posture for
  this class of tool, not a novel risk.

## Consequences

- **`Flare.Api` can only run as a single replica.** SQLite doesn't support
  multiple processes writing to the same file across a network filesystem
  safely, and Flare's SQLite file lives on a local volume, not something
  horizontally scaled replicas could safely share. This is a real
  constraint if the (currently unscheduled) Kubernetes/Helm roadmap item
  ever needs more than one `Flare.Api` pod.
- If that day comes, **migrating to Postgres is a contained, mechanical
  follow-up, not a rewrite**: the affected tables are `Users`/`Sessions`/
  `IngestApiKeys`/`AuthSettings`/`EntraSettings`/`LdapSettings`/
  `OidcSettings`/`ProxyAuthSettings`/`schema_migrations` — nothing outside
  identity/auth depends on SQLite specifically.
- Every auth method's fine-grained settings (Entra/LDAP/OIDC/reverse-proxy
  config) live exclusively in this database, configured through the `/auth`
  dashboard page rather than config files — a direct consequence of picking
  a database that's naturally per-instance and admin-editable, rather than
  a config-file model. See `docs/explanation/authentication-model.md`.

## Related documentation

- `docs/explanation/authentication-model.md` — where this fits in the
  overall auth model
- `docs/reference/authentication-config.md` — the config keys that remain
  file-based vs. database-only