# ADR-0282: Least-privilege ClickHouse users per service

Status: Accepted (not implemented yet, see the roadmap)
Date: 2026-11-27

## Context

All Flare services connect to ClickHouse as the image's `default` user:
- Flare.Ingest, Flare.Api and Flare.AlertWorker
- both migration runners
- every install path: `docker-compose*.yml`, the `flare` CLI templates, Helm (`flare.clickhouseUser`) and the Aspire
  AppHost (`AddClickHouse`)

That user can do anything. It can DROP tables, read every `system.*` table, and call `url()`, `file()`, `remote()` and
`s3()`.

Flare.Api turns user input into SQL in several builders: LogQL, PromQL, TraceQL, structural trace queries, and log/span
search. One escaping bug in any of them would give an attacker full database access, plus server-side requests and
local file reads from the ClickHouse host. ClickHouse can block all of that with grants, settings constraints and
profiles. See [the investigation](../investigations/clickhouse-access-control-vs-identity-store.md), which also records
why ClickHouse can't replace `Flare.Identity`.

Each service needs different privileges:
- **Ingest:** INSERT on the data tables, and SELECT on six config tables (`pipeline_rules`, `log_metrics`,
  `metric_attribute_rules`, `runtime_settings`, `telemetry_exports`, `checkin_monitors`).
- **Api, for queries:** SELECT only.
- **Api, for its other work:**
  - SELECT and INSERT on the config tables
  - runtime DDL, namely retention (`MODIFY TTL`, `MODIFY COLUMN`, `MODIFY SETTING storage_policy`), promoted columns
    (`ADD COLUMN`, `MATERIALIZE`) and erasure (`ALTER ... DELETE`)
  - `ON CLUSTER` in cluster mode
- **AlertWorker:** SELECT, plus INSERT into `metrics_gauge` and alert state.
- **Migrations,** which Ingest and Api both run at startup: any DDL.

## Decision

- **Flare ships the ClickHouse users as a server config file, not as SQL.** `db/clickhouse/config/flare-users.xml` is
  mounted at `/etc/clickhouse-server/users.d/`. Each user is declared there with `<password from_env="..."/>` and
  `<grants><query>GRANT ...</query></grants>`. ClickHouse reads `users.d` at every start. So the file reaches existing
  volumes (unlike `docker-entrypoint-initdb.d`) and needs no `access_management` on any credential. In cluster mode the
  same file is mounted on every node, which is also needed because the inter-server `<secret>` runs a forwarded query as
  the user who sent it.
- **Three runtime users:**

  | User | Used by | Privileges |
  |---|---|---|
  | `flare_ingest` | Ingest | INSERT on `clickhousedb.*`, SELECT on the six config tables |
  | `flare_query` | Every query service in Api that runs generated SQL | SELECT on `clickhousedb.*` |
  | `flare_app` | Api's config writes, retention, promoted columns and erasure; AlertWorker | SELECT and INSERT on `clickhousedb.*`; the ALTER subtypes listed in Context; `CLUSTER` |

  None of them gets `SOURCES` (the table functions), `SYSTEM`, `DROP`, `CREATE`, or `access_management`. `flare_query`
  can still read `system.*` metadata, which the indexing-health and promoted-column views use. That access is governed
  by the server's `select_from_system_db_requires_grant`, which defaults to off.
- **`flare_query` gets a settings profile.** It sets `readonly=2`, so `QuerySafety`'s per-query settings keep working;
  `readonly=1` would reject them. It also sets ceilings: `max_execution_time` at most 300 and `max_rows_to_read` at most
  10,000,000,000. `QuerySafety` still sets the effective caps per query, and the ceiling only stops a code path that
  forgets them. A value over the ceiling is an error (code 452), not a clamp. So the ceilings sit well above the
  `Query__*` defaults (30 s and 1,000,000,000 rows), retention's 600 s query runs as `flare_app`, and the reference docs
  must say that raising `Query__*` past the ceiling means editing the file. `max_result_rows` gets no ceiling, because
  some builders raise it on purpose (`SpanQueryService`).
- **The existing connection string becomes the DDL credential.** `ConnectionStrings:clickhousedb` (the `default` user)
  is used only by `ClickHouseMigrationRunner` at startup. The new connection strings are
  `ConnectionStrings:clickhousedb-ingest`, `clickhousedb-query` and `clickhousedb-app`. Each one falls back to
  `ConnectionStrings:clickhousedb` when it isn't set. This keeps existing installs working, and also installs whose own
  or Cloud ClickHouse has no Flare users.
- **New installs get this by default.** The changes per install path:
  - **Compose files and CLI templates:** mount the file, and pass `FLARE_CH_INGEST_PASSWORD`, `FLARE_CH_QUERY_PASSWORD`
    and `FLARE_CH_APP_PASSWORD` both to ClickHouse and to the services. The CLI generates the passwords the same way it
    generates the others.
  - **Helm:** a ConfigMap, plus Secret keys, when `clickhouse.enabled`. With `clickhouse.external`, the operator supplies
    the connection strings.
  - **Aspire AppHost:** a bind mount, plus secret parameters.
- **Moving an existing install over** means mounting the file, setting the three password variables, restarting
  ClickHouse, and then setting the new connection strings. An install that skips this keeps working on `default`.

## Alternatives considered

- **`CREATE USER` SQL run by the migration runner.** This needs `access_management=1` on the `default` user, which the
  image turns off unless `CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT=1` is set. In cluster mode it also needs replicated access
  storage or `ON CLUSTER`. On top of that, a credential that can create users can mint new superusers. Rejected.
- **One shared non-default user.** This removes `SOURCES`, `SYSTEM` and `DROP`, but Ingest could still read all
  telemetry and generated SQL could still write. It's half the benefit for nearly the same setup cost. Rejected.
- **Moving migrations to a one-shot job,** so that no long-running service holds the DDL credential. This is the
  stronger isolation. But it needs a job concept on all three install paths (a run-once Compose service, a Helm hook Job,
  an Aspire resource), and it changes today's guarantee that each service brings the schema up itself at startup. It's
  deferred, not rejected (see Consequences).
- **Row policies for project scoping in the same change.** They would sit on top of `flare_query`, but they are a
  separate decision with their own costs: a policy on every `_local` and aggregate table, the policy key coming from a
  setting the client controls, and failing closed. They're on the roadmap as an opt-in.

## Consequences

- **What this protects against, and what it doesn't.** Injected SQL in a generated query can only read. It can't write,
  run DDL, call `url()` or `file()`, run `SYSTEM` commands, or run past the time and row ceilings. Ingest can't read
  telemetry. But a compromised Api or Ingest process still has the DDL password in its environment until migrations
  move to a separate job.
- **Api needs two ClickHouse clients,** registered as keyed services. Every query service has to be on the `query`
  client. A query service left on `app` doesn't break anything; it just keeps more privilege than it needs. A store
  mistakenly put on `query` fails loudly with `ACCESS_DENIED`. Unit tests can't see either mistake, so the
  implementation PR needs a live run of every page and API against the split users.
- **New migrations can need grant changes.** A migration that adds a table Ingest reads, or a new runtime ALTER in Api,
  must also update `flare-users.xml`. Missing it shows up as `ACCESS_DENIED` in the end-to-end run, not as a silent
  leak.
- **The exact grant names** for the ALTER subtypes (for example the one covering `MATERIALIZE COLUMN`) have to be
  confirmed by a probe against the pinned image when this is implemented.
- **This is the base for later work,** on the roadmap: per-identity quotas keyed by `quota_key`, opt-in row policies,
  and a documented read-only user for running SQL directly against ClickHouse.

## Related documentation

- [Investigation: ClickHouse access control vs the identity store](../investigations/clickhouse-access-control-vs-identity-store.md)
- ADR-0003 (Distributed tables and the inter-server secret), ADR-0013 (ClickHouse as storage), ADR-0111 (identity
  providers), ADR-0123 (project scoping)
- [ClickHouse access rights](https://clickhouse.com/docs/concepts/features/security/access-rights),
  [settings constraints](https://clickhouse.com/docs/operations/settings/constraints-on-settings)
