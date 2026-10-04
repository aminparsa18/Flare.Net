# ADR-0111: Pluggable identity store, with an optional PostgreSQL provider

Status: Accepted

Date: 2026-10-04

Supersedes [ADR-0004](0004-embedded-sqlite-for-identity.md) in part: SQLite stays the default,
but is no longer the only option.

## Context

ADR-0004 put users, sessions, ingest API keys and every auth setting in an embedded SQLite file
to keep the stack small, and recorded the cost: SQLite cannot be written by processes on
different hosts, so `Flare.Api` is pinned to one replica and the identity volume to one node.
Cluster mode (ADR-0003 and the clustering docs) made ClickHouse and `Flare.Ingest` horizontally
scalable, which left the identity store as the one piece of the control plane that was not. The
default install must stay as light as before.

## Decision

- **Two providers behind the same stores.** `Identity:Provider` is `Sqlite` (default,
  `Identity:DbPath`, unchanged behaviour) or `Postgres` (`Identity:ConnectionString`, an Npgsql
  connection string). `IdentityDbConnectionFactory` hands out a provider-neutral `DbConnection`.
- **One set of stores, one SQL dialect.** The `Sqlite*Store` classes are now `Db*Store`
  (`DbUserStore`, ...) and use only `System.Data.Common` types. This was cheap because the schema
  was already portable: timestamps are ISO-8601 `TEXT`, booleans are 0/1 integers, ids are
  GUID `TEXT`. The remaining differences were removed from the shared SQL rather than branched
  on: `@name` parameters (both engines accept them), `INSERT ... ON CONFLICT DO NOTHING` instead
  of `INSERT OR IGNORE`, and `LOWER(col) = LOWER(@x)` instead of `COLLATE NOCASE`. Integers are
  always read with `GetInt64` because the Postgres schema uses `BIGINT`.
- **Per-provider migrations.** `Migrations/Sqlite/` is the existing 0001-0026 history.
  `Migrations/Postgres/` starts with one `0001_baseline.sql` holding the final schema (no Postgres
  deployment predates it, so replaying SQLite's table rebuilds would be pointless). From now on a
  schema change adds a numbered file to **both** folders. Case-insensitive uniqueness
  (`Users.Username`, `LlmModelPrices.Model`) is a unique index on `LOWER(...)` in Postgres.
- **Migration races.** SQLite keeps `BEGIN IMMEDIATE`. Postgres takes a transaction-scoped
  advisory lock (`pg_advisory_xact_lock`) before reading `schema_migrations`, so Ingest and any
  number of Api replicas can start together; DDL is transactional, so the batch is still atomic.
- **No data migration tool.** Switching an existing install from SQLite to Postgres starts with an
  empty identity database (the first-run bootstrap creates a new admin). Moving users, keys and
  settings across is left out until someone asks for it.
- **Cluster mode uses Postgres.** `docker-compose.cluster.yml` gains a `postgres` service and
  `ingest-1`, `ingest-2` and `api` use it; the `identity-data` volume is gone from that file. The
  base `docker-compose.yml`, the Aspire AppHost and the `flare` CLI stack stay on SQLite.

## Consequences

- The identity store no longer ties `Flare.Api` to one host. This ADR does **not** make running
  several `Flare.Api` replicas supported: some state is still per process (the PAT rate limiter,
  ADR-0028, becomes per-replica; Entra/OIDC options are read once at startup). The cluster compose
  file still runs one `api`. Lifting those is separate work.
- Postgres is an extra container only for people who opt in; the default footprint is unchanged.
- Two migration folders must be kept in step by hand. The store tests catch drift: the whole
  `Flare.Identity.Tests` suite runs against Postgres when `FLARE_TEST_POSTGRES` is set to an
  Npgsql connection string for a server where the user can `CREATE DATABASE`, each test in its own
  throwaway database. CI only runs the SQLite pass unless it provides that variable.
- `Flare.Ingest` still caches ingest keys in memory and polls the store (ADR-0004), so a Postgres
  outage keeps ingestion running on the last known key set.
