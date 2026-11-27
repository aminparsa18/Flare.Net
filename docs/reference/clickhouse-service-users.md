# ClickHouse service users

The ClickHouse users Flare's services connect as, what each one may do, and the
settings that wire them up. To switch an existing install over, see
[How to use the ClickHouse service users](../how-to/admin/use-clickhouse-service-users.md).
For why, see [ADR-0282](../../docs-internal/adr/0282-clickhouse-service-users.md)
and [ADR-0283](../../docs-internal/adr/0283-clickhouse-service-users-implementation.md).

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
| `default` | The migration runner in Flare.Ingest and Flare.Api, at startup | Everything. Flare uses it only for DDL. |
| `flare_ingest` | Flare.Ingest | `INSERT` on `clickhousedb.*`. `SELECT` on `pipeline_rules`, `log_metrics`, `metric_attribute_rules`, `runtime_settings`, `telemetry_exports` and `checkin_monitors` only (cluster file: also their `_local` tables). |
| `flare_query` | Flare.Api's query services: log, span, metric and profile search, LogQL, PromQL, TraceQL, and every page's charts | `SELECT` on `clickhousedb.*`. Settings profile `flare_query` (below). |
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

## Connection strings

| Key | Env var | Used by | When unset |
|---|---|---|---|
| `ConnectionStrings:clickhousedb` | `ConnectionStrings__clickhousedb` | The migration runner in Flare.Ingest and Flare.Api (`default`) | Startup fails |
| `ConnectionStrings:clickhousedb-ingest` | `ConnectionStrings__clickhousedb-ingest` | Flare.Ingest (`flare_ingest`) | Falls back to `clickhousedb` |
| `ConnectionStrings:clickhousedb-query` | `ConnectionStrings__clickhousedb-query` | Flare.Api's query services (`flare_query`) | Falls back to `clickhousedb` |
| `ConnectionStrings:clickhousedb-app` | `ConnectionStrings__clickhousedb-app` | Flare.Api's other work and Flare.AlertWorker (`flare_app`) | Falls back to `clickhousedb` |

An empty value counts as unset. Flare.AlertWorker runs no migrations: it
needs `clickhousedb-app`, or `clickhousedb` as the fallback.

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
| `clickhouse.external.usersSecret` | `""` | External ClickHouse only. Secret with the full connection strings under the keys `clickhousedb-ingest`, `clickhousedb-query` and `clickhousedb-app`. A missing key falls back to `clickhouse.external.username` and the ClickHouse password. |
