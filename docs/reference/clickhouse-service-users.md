# ClickHouse service users

The ClickHouse users Flare's services connect as, what each one may do, and the
settings that wire them up. To switch an existing install over, see
[How to use the ClickHouse service users](../how-to/admin/use-clickhouse-service-users.md).
For why, see [ADR-0282](../../docs-internal/adr/0282-clickhouse-service-users.md)
[ADR-0283](../../docs-internal/adr/0283-clickhouse-service-users-implementation.md)
[ADR-0284](../../docs-internal/adr/0284-clickhouse-migration-job.md)
and [ADR-0286](../../docs-internal/adr/0286-per-identity-clickhouse-quotas.md).

## Users

The users are declared in a server config file that is mounted at
`/etc/clickhouse-server/users.d/flare-users.xml`:

| File | Used by |
|---|---|
| `db/clickhouse/config/flare-users.xml` | Single-node ClickHouse: `docker-compose.yml`, `docker-compose.install.yml`, `flare start`, the Aspire AppHost |
| `db/clickhouse-cluster/config/flare-users.xml` | Every node of a cluster: `docker-compose.cluster.yml`, `flare start --cluster` |
| `deploy/helm/flare/files/flare-users.xml` | Helm, bundled ClickHouse (a copy of the single-node file) |

| User | Connects | Privileges |
|---|---|---|
| `default` | The migration runner: the one-shot migration job, or Flare.Ingest and Flare.Api at startup (see [Where migrations run](#where-migrations-run)) | Everything. Flare uses it only for DDL. |
| `flare_ingest` | Flare.Ingest | `INSERT` on `clickhousedb.*`. `SELECT` on `pipeline_rules`, `log_metrics`, `metric_attribute_rules`, `runtime_settings`, `telemetry_exports` and `checkin_monitors` only (cluster file: also their `_local` tables). `SELECT` on the `Name` column of `schema_migrations`, for the wait below. |
| `flare_query` | Flare.Api's query services: log, span, metric and profile search, LogQL, PromQL, TraceQL, and every page's charts | `SELECT` on `clickhousedb.*`. Settings profile `flare_query` and quota `flare_query` (below). |
| `flare_app` | Everything else in Flare.Api (config writes, retention, promoted columns, erasure, the Indexing page, cluster status), and Flare.AlertWorker | `SELECT` and `INSERT` on `clickhousedb.*`; `ALTER TTL`, `ALTER MODIFY COLUMN`, `ALTER SETTINGS`, `ALTER ADD COLUMN`, `ALTER DROP COLUMN`, `ALTER ADD INDEX`, `ALTER DROP INDEX`, `ALTER MATERIALIZE COLUMN`, `ALTER MATERIALIZE INDEX` and `ALTER DELETE` on `clickhousedb.*`; `SELECT` on the system tables listed below; `CREATE TEMPORARY TABLE` and `WRITE ON S3` (archive export). Cluster file only: `CLUSTER` and `READ ON REMOTE`. |

None of the three service users can `CREATE`, `DROP`, `TRUNCATE` or run
`SYSTEM` commands, and none can manage users. Only `flare_app` gets a table
function, and only as listed above. `url()`, `file()`, `remote()`, reading
from `s3()` and the rest are denied.

The system tables `flare_app` can read are `tables`, `mutations`, `parts`, `part_log`,
`query_log`, `disks`, `storage_policies`, `data_skipping_indices`, `clusters`,
`replicas` and `replication_queue`. The `clickhouse/clickhouse-server` image
enables `select_from_system_db_requires_grant`, so every other user only sees
`system.tables`, `system.columns`, `system.databases` and the constant tables.

### The `flare_query` settings profile

| Setting | Value | Effect |
|---|---|---|
| `readonly` | `2` | Read-only. Queries can still change settings, which Flare's per-query caps need. A query can't change `readonly` itself. |
| `max_execution_time` | at most `300` | A query that asks for more fails with code 452 (`SETTING_CONSTRAINT_VIOLATION`). |
| `max_rows_to_read` | at most `10000000000` | Same. |

Flare.Api sets the effective caps on every query from the `Query:*` settings
(defaults: 30 seconds and 1,000,000,000 rows). The profile's ceilings only
stop a code path that forgets them. A `0` (unlimited) or anything above a
ceiling in `Query:MaxExecutionSeconds` or `Query:MaxRowsToRead` makes every
query fail with code 452, so raise the ceiling in the users file as well.

### The `flare_query` quota

A keyed quota with a one-hour interval. Flare.Api sends each query's key in the
`X-ClickHouse-Quota` header, so ClickHouse counts each caller separately.

| Caller | Quota key |
|---|---|
| A signed-in account | `user:{user id}` |
| A personal access token | `pat:{token id}`, separate from its owner's |
| A demo-mode visitor | `demo:{client address}` |
| Unauthenticated requests (auth disabled), Flare.Api's background work | none: the shared `flare_query` bucket |

Every limit in `flare-users.xml` is `0`, which means no limit. ClickHouse
still counts each key's queries, rows read and execution time in
`system.quotas_usage`. To set limits, see
[Cap ClickHouse query cost per account](../how-to/admin/use-clickhouse-service-users.md#cap-clickhouse-query-cost-per-account).

| Limit | Counts |
|---|---|
| `read_rows` | Rows read by the key's queries in the hour |
| `execution_time` | Seconds the key's queries ran in the hour, added together |
| `queries`, `errors`, `result_rows` | Also available, `0` in the shipped file |

A query over a limit fails with code 201 (`QUOTA_EXCEEDED`), and Flare.Api
answers the request with `429 Too Many Requests`. Flare.AlertWorker and
Flare.Api's `flare_app` work aren't counted.

## Connection strings

| Key | Env var | Used by | When unset |
|---|---|---|---|
| `ConnectionStrings:clickhousedb` | `ConnectionStrings__clickhousedb` | The migration runner (`default`), and Flare.AlertWorker images up to 0.6.0 under Helm's startup-migration fallback | Flare.Ingest and Flare.Api wait for the migration job instead of running migrations. The three keys below then have nothing to fall back to. |
| `ConnectionStrings:clickhousedb-ingest` | `ConnectionStrings__clickhousedb-ingest` | Flare.Ingest (`flare_ingest`) | Falls back to `clickhousedb` |
| `ConnectionStrings:clickhousedb-query` | `ConnectionStrings__clickhousedb-query` | Flare.Api's query services (`flare_query`) | Falls back to `clickhousedb` |
| `ConnectionStrings:clickhousedb-app` | `ConnectionStrings__clickhousedb-app` | Flare.Api's other work and Flare.AlertWorker (`flare_app`) | Falls back to `clickhousedb` |

An empty value counts as unset. Flare.AlertWorker runs no migrations: it
needs `clickhousedb-app`, or `clickhousedb` as the fallback. When Helm falls
back to startup migrations (below), it still gives AlertWorker `clickhousedb`,
because AlertWorker images up to 0.6.0 read only that key. Newer images use
`clickhousedb-app` when it's set, so they still connect as `flare_app`.

## Password variables

The users file reads each password from an environment variable on the
ClickHouse container.

| Variable | User | Docker Compose default | `install.sh`, `flare start` | Helm (bundled ClickHouse) | Aspire AppHost parameter |
|---|---|---|---|---|---|
| `FLARE_CH_INGEST_PASSWORD` | `flare_ingest` | `flare` | Random | Random, Secret key `clickhouse-ingest-password` | `clickhouse-ingest-password` |
| `FLARE_CH_QUERY_PASSWORD` | `flare_query` | `flare` | Random | Random, Secret key `clickhouse-query-password` | `clickhouse-query-password` |
| `FLARE_CH_APP_PASSWORD` | `flare_app` | `flare` | Random | Random, Secret key `clickhouse-app-password` | `clickhouse-app-password` |

> **Warning:** if one of these variables isn't set on the ClickHouse
> container, ClickHouse gives that user an **empty password** and only logs
> `Env variable is not set`. Set all three wherever you mount the file.

Unlike `CLICKHOUSE_PASSWORD`, which the image applies only on first init,
these are read at every ClickHouse start. Changing one needs a restart of
ClickHouse and of the services that use it.

## Helm values

| Value | Default | Meaning |
|---|---|---|
| `clickhouse.users.enabled` | `true` | Bundled ClickHouse only. Mounts `files/flare-users.xml`, generates the three passwords, and points each service at its own user. `false` keeps every service on `default`. |
| `clickhouse.external.usersSecret` | `""` | External ClickHouse only. Secret with the full connection strings under the keys `clickhousedb-ingest`, `clickhousedb-query` and `clickhousedb-app`. A missing key falls back to `clickhouse.external.username` and the ClickHouse password, except when the migration Job runs, where all three keys are required. |
| `clickhouse.queryQuota.readRows` | `0` | Bundled ClickHouse with `clickhouse.users.enabled` only. The hourly `read_rows` limit per quota key; `0` = no limit. |
| `clickhouse.queryQuota.executionTimeSeconds` | `0` | The same, for `execution_time` in seconds. When either value is above `0`, the chart mounts `users.d/zz-flare-query-quota.xml` with both. |
| `clickhouse.migrationJob.enabled` | `true` | Run migrations in a one-shot Job and give no service pod the `default` password (below). The chart falls back to migrating at startup, and says why in the install notes, when `image.tag` is a release older than 0.7.0, or when neither `clickhouse.users.enabled` (bundled ClickHouse) nor `clickhouse.external.usersSecret` gives the services their own users. `false` always migrates at startup. |
| `clickhouse.migrationJob.resources` | `{}` | The Job container's resources. |
| `clickhouse.migrationJob.ttlSecondsAfterFinished` | `86400` | How long Kubernetes keeps a finished Job. |

## Where migrations run

A process applies the ClickHouse migrations at startup only when it has
`ConnectionStrings:clickhousedb`. Without it, Flare.Ingest and Flare.Api wait
until `clickhousedb.schema_migrations` lists every migration their build
contains, then start. The migration job applies them: the Api image run as
`dotnet Flare.Api.dll migrate` (the image's entrypoint plus the argument
`migrate`). It needs `ConnectionStrings__clickhousedb`, and in cluster mode
`ClickHouse__ClusterMode=true`. It exits `0` once the schema is current, `1`
when a migration fails, and `2` when the connection string is missing.

| Install path | Where migrations run |
|---|---|
| `docker-compose.yml`, `docker-compose.cluster.yml` | The `clickhouse-migrate` service. `ingest`, `api` and `alert-worker` start after it exits `0`. |
| Aspire AppHost | The `clickhouse-migrate` resource. The services wait for it to finish. |
| `docker-compose.install.yml` (`install.sh`) | The `clickhouse-migrate` service, as above. For a `FLARE_IMAGE_TAG` older than 0.7.0, `install.sh` fetches that release's files instead, which migrate in ingest and api at startup. |
| `flare start` | The `clickhouse-migrate` service, for instances created by Flare.Cli 0.1.12 or later. `flare start` and `flare update` refuse a `FLARE_IMAGE_TAG` older than 0.7.0 for them. An instance created by an older CLI keeps its compose file and migrates in ingest and api at startup. |
| Helm | A Job per release revision. In ingest and api at startup when the chart falls back (see `clickhouse.migrationJob.enabled` above). |

The Api image has the `migrate` command from Flare 0.7.0. An older one, run
as the job, starts the whole API instead of exiting, so the services that wait
for the job never start. Tags that aren't a version number, such as `edge`,
`latest` or `sha-…`, count as new.

| Key | Env var | Default | Meaning |
|---|---|---|---|
| `ClickHouse:SchemaWaitTimeoutSeconds` | `ClickHouse__SchemaWaitTimeoutSeconds` | `300` | How long Flare.Ingest and Flare.Api wait for the migration job. After that they exit, naming the first missing migration, so the orchestrator restarts them. |

The wait reads `schema_migrations` as the service's own user. Under a
`flare-users.xml` older than ADR-0284, `flare_ingest` lacks that grant and
Flare.Ingest fails at startup with `ACCESS_DENIED`. Update the file, or keep
giving Flare.Ingest `ConnectionStrings:clickhousedb`.
