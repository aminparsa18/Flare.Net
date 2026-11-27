# ADR-0284: Run ClickHouse migrations as a one-shot job

Status: Accepted

Date: 2026-11-27

## Context

[ADR-0282](0282-clickhouse-service-users.md) moved every runtime query onto a least-privilege ClickHouse user, but
left the migrations in Flare.Ingest and Flare.Api at startup. Both processes therefore still carry
`ConnectionStrings:clickhousedb`, the `default` user's password, for their whole lifetime. A compromised Ingest
or Api process can read that password from its own environment and get full DDL, `SYSTEM` and table-function
access. ADR-0282 deferred the fix, because it needs a run-once concept on every install path and changes the
guarantee that each service brings the schema up itself.

Two install paths run published images instead of building from source: `docker-compose.install.yml` (used by
`install.sh`) and the `flare` CLI's Compose templates, whose default image is 0.6.0. Helm defaults to the
chart's `appVersion`, and an operator can pin an older `image.tag`. None of those images knows a migration
command, and ADR-0283 already established that a template must not strip a connection string an older image
still needs.

## Decision

- **The job is the Api image run as `migrate`.** `dotnet Flare.Api.dll migrate` (the image's entrypoint plus one
  argument) builds a minimal host, applies every pending migration as `ConnectionStrings:clickhousedb` through
  the existing `ClickHouseMigrationRunner`, and exits: `0` when the schema is current, `1` when a migration fails,
  `2` without the connection string. The migration SQL is already embedded in `Flare.ServiceDefaults`, which
  every image carries, so the job always matches its Api version.
- **The DDL connection string decides who migrates.** Flare.Ingest and Flare.Api apply migrations at startup
  only when `ConnectionStrings:clickhousedb` is set, as before. Without it they don't register the DDL client,
  and wait until `clickhousedb.schema_migrations` lists every migration their build embeds. They poll every 2
  seconds, for up to `ClickHouse:SchemaWaitTimeoutSeconds` (default 300), then exit naming the first missing
  migration, so an orchestrator restarts them. Extra applied rows are fine: a newer job may be ahead of an older
  service. There's no new mode setting. A process that doesn't hold the DDL password can't run DDL, so it waits.
- **`flare_ingest` gets `SELECT(Name) ON clickhousedb.schema_migrations`.** It's a column grant, so Ingest
  still can't read any telemetry. `flare_app`, which Api uses for the wait, already has `SELECT` on
  `clickhousedb.*`. All three users files carry the grant. The grant doesn't start with
  `GRANT SELECT ON clickhousedb.`, so the parity test doesn't expect a `_local` copy, which is right:
  `schema_migrations` is one replicated table, not a Distributed one.
- **Per install path:**
  - **Root Compose files** (`docker-compose.yml`, `docker-compose.cluster.yml`, and the cold-storage overlays on
    top of them): a `clickhouse-migrate` service builds the Api Dockerfile with `command: ["migrate"]`, and is the
    only service with `ConnectionStrings__clickhousedb`. `ingest`, `api` and `alert-worker` depend on it with
    `service_completed_successfully`. In cluster mode the ON CLUSTER migrations now run once, instead of
    concurrently from three processes.
  - **Aspire AppHost:** a `clickhouse-migrate` resource (`AddDotnetProject` on Flare.Api with
    `ExcludeLaunchProfile`, so it doesn't claim the Api's port, and `WithArgs("migrate")`) holds the only
    `clickhousedb` reference. The services `WaitForCompletion` on it.
  - **Helm:** `clickhouse.migrationJob.enabled`, off by default. When on, a plain Job named
    `<release>-flare-clickhouse-migrate-<revision>` runs the api image with `args: ["migrate"]`, and no other pod
    gets `CLICKHOUSE_PASSWORD` or `ConnectionStrings__clickhousedb`, alert-worker included (job mode needs a new
    image, so ADR-0283's old-image fallback doesn't apply). The chart refuses to render job mode without
    per-user connection strings, and `external.usersSecret` keys stop being `optional`.
  - **`docker-compose.install.yml` and the CLI templates:** unchanged. They run published images, and 0.6.0 has
    no `migrate` command.

## Alternatives considered

- **A separate `Flare.Migrations` project and image.** It would be a smaller image for the job, but it's a
  fourth image to build, publish, pin in Helm and the CLI, and keep in version lockstep with Api. Rejected.
- **A Helm hook Job,** as the roadmap item said. `pre-install` runs before the chart's own Secret and bundled
  ClickHouse exist. `post-install`/`post-upgrade` hooks run only after Helm considers the release ready, so under
  `helm install --wait` they deadlock: ingest and api aren't ready until the hook has run. A plain Job with the
  revision in its name avoids both, at the cost of Kubernetes, not Helm, sequencing the startup (the services
  wait, then restart).
- **An explicit `ClickHouse:Migrations` mode setting** (`Startup` / `External`). Rejected: it can disagree with
  the credentials actually given. A process told to migrate without the password fails anyway, and a process
  told to wait while holding it defeats the point.
- **Switching `docker-compose.install.yml`, the CLI and Helm to the job by default now.** Rejected for now: with
  0.6.0 images the job would start the full API instead of exiting, the dependent services would wait forever,
  and Ingest/Api 0.6.0 without `clickhousedb` crash. They flip once a release ships the `migrate` command (on
  the roadmap).
- **Moving Identity migrations into the job as well.** Out of scope. They don't use the ClickHouse DDL
  credential, and with SQLite the job would have to mount the Identity volume too.

## Consequences

- On the job-based paths, a compromised Ingest, Api or AlertWorker process no longer has the `default` password.
  Only the job and ClickHouse itself see it.
- A `flare-users.xml` older than this ADR, paired with a services-without-DDL setup, fails Flare.Ingest's
  startup with `ACCESS_DENIED` on `schema_migrations`. That's loud, and the reference doc says so.
- Startup order now matters on the job paths: an Api or Ingest image newer than the job waits and then
  restarts until the matching job runs. In Helm the image tag is shared, so they move together.
- `ClickHouseMigrationRunner` stays safe to run concurrently, so a mixed setup (job plus a service still holding
  `clickhousedb`) works; it just keeps the password where the job was meant to remove it.

## Related documentation

- [Reference: ClickHouse service users, "Where migrations run"](../../docs/reference/clickhouse-service-users.md#where-migrations-run)
- [ADR-0282](0282-clickhouse-service-users.md), [ADR-0283](0283-clickhouse-service-users-implementation.md)
