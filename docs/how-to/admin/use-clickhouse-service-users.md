# How to use the ClickHouse service users

Make an existing Flare install connect to ClickHouse as three least-privilege
users instead of `default`. Flare.Ingest becomes `flare_ingest`, Flare.Api's
queries become `flare_query`, and the rest of Flare.Api and Flare.AlertWorker
become `flare_app`. `default` then only runs migrations. With this
in place, a bug in the SQL that Flare builds from a request can only read
data. It can't write, drop tables or reach other hosts. For the exact grants,
see [the ClickHouse service users reference](../../reference/clickhouse-service-users.md).

Installs created with this version already work this way: a new
`docker compose up`, `install.sh` install, `flare start` instance, Helm
release with the bundled ClickHouse, or Aspire AppHost run. An install you
don't move over keeps running everything as `default`.

## Prerequisites

- Flare images from this version or later.
- Access to the ClickHouse container's environment and volumes, or to the
  ClickHouse server's users config. For ClickHouse Cloud or a server you
  can't mount files into, follow [Your own ClickHouse server](#your-own-clickhouse-server).

## Docker Compose or a `flare` CLI instance

`flare start` never rewrites an instance's `docker-compose.yml` or `.env`, so
an instance created before this version needs the same edits. Its files are
in `~/.flare/` (or `~/.flare/instances/<NAME>/`), and `flare start` extracts
`db/clickhouse/config/flare-users.xml` (or the cluster file) next to them.

1. Add three passwords to `.env`. Use letters and digits only, because the
   values go into connection strings:

   ```bash
   for v in FLARE_CH_INGEST_PASSWORD FLARE_CH_QUERY_PASSWORD FLARE_CH_APP_PASSWORD; do
     echo "$v=$(openssl rand -hex 16)" >> .env
   done
   ```

2. In `docker-compose.yml`, give the `clickhouse` service (each
   `clickhouse-N` service in cluster mode) the passwords and the users file:

   ```yaml
   environment:
     FLARE_CH_INGEST_PASSWORD: ${FLARE_CH_INGEST_PASSWORD}
     FLARE_CH_QUERY_PASSWORD: ${FLARE_CH_QUERY_PASSWORD}
     FLARE_CH_APP_PASSWORD: ${FLARE_CH_APP_PASSWORD}
   volumes:
     # Cluster mode: ./db/clickhouse-cluster/config/flare-users.xml
     - ./db/clickhouse/config/flare-users.xml:/etc/clickhouse-server/users.d/flare-users.xml:ro
   ```

   Set all three variables. ClickHouse gives a user whose variable is unset
   an empty password.

3. Add the connection strings, using your file's existing
   `ConnectionStrings__clickhousedb` as the pattern. Keep that one on
   `ingest` and `api`; the migrations still need it, unless a one-shot
   migration job runs them (see
   [Where migrations run](../../reference/clickhouse-service-users.md#where-migrations-run)).

   | Service | Add |
   |---|---|
   | `ingest` (each `ingest-N`) | `ConnectionStrings__clickhousedb-ingest: "Host=clickhouse;Port=8123;Database=clickhousedb;Username=flare_ingest;Password=${FLARE_CH_INGEST_PASSWORD}"` |
   | `api` | `ConnectionStrings__clickhousedb-query: "...;Username=flare_query;Password=${FLARE_CH_QUERY_PASSWORD}"` and `ConnectionStrings__clickhousedb-app: "...;Username=flare_app;Password=${FLARE_CH_APP_PASSWORD}"` |
   | `alert-worker` | Add `ConnectionStrings__clickhousedb-app: "...;Username=flare_app;Password=${FLARE_CH_APP_PASSWORD}"`. Keep `ConnectionStrings__clickhousedb` if the image is 0.6.0 or older: those read only that key. Newer images use `clickhousedb-app` |

   In cluster mode the host is `clickhouse-lb`, as in the existing string.

4. Recreate the stack: `docker compose up -d` (or `flare stop`, then `flare start`).
   ClickHouse reads `users.d` when it starts, so it picks the users up on the
   existing data volume.

## Helm with the bundled ClickHouse

`helm upgrade` does it: `clickhouse.users.enabled` defaults to `true`. The
chart generates the three passwords into its Secret, mounts the users file,
restarts ClickHouse, and points each service at its own user. To stay on
`default`, set `clickhouse.users.enabled=false`.

## Your own ClickHouse server

This covers `clickhouse.enabled=false` in Helm, ClickHouse Cloud, and any
server whose config you can't mount. Create the users with SQL, as a user that
has `access_management`:

```sql
CREATE USER flare_ingest IDENTIFIED BY '...';
GRANT INSERT ON clickhousedb.* TO flare_ingest;
GRANT SELECT ON clickhousedb.pipeline_rules TO flare_ingest;
GRANT SELECT ON clickhousedb.log_metrics TO flare_ingest;
GRANT SELECT ON clickhousedb.metric_attribute_rules TO flare_ingest;
GRANT SELECT ON clickhousedb.runtime_settings TO flare_ingest;
GRANT SELECT ON clickhousedb.telemetry_exports TO flare_ingest;
GRANT SELECT ON clickhousedb.checkin_monitors TO flare_ingest;

CREATE SETTINGS PROFILE flare_query SETTINGS
    readonly = 2,
    max_execution_time = 30 MAX 300,
    max_rows_to_read = 1000000000 MAX 10000000000;
CREATE USER flare_query IDENTIFIED BY '...' SETTINGS PROFILE 'flare_query';
GRANT SELECT ON clickhousedb.* TO flare_query;
CREATE QUOTA flare_query KEYED BY client_key, user_name FOR INTERVAL 1 hour TRACKING ONLY TO flare_query;

CREATE USER flare_app IDENTIFIED BY '...';
GRANT SELECT, INSERT ON clickhousedb.* TO flare_app;
GRANT ALTER TTL, ALTER MODIFY COLUMN, ALTER SETTINGS, ALTER ADD COLUMN, ALTER DROP COLUMN,
    ALTER ADD INDEX, ALTER DROP INDEX, ALTER MATERIALIZE COLUMN, ALTER MATERIALIZE INDEX,
    ALTER DELETE ON clickhousedb.* TO flare_app;
GRANT SELECT ON system.tables, SELECT ON system.mutations, SELECT ON system.parts, SELECT ON system.part_log,
    SELECT ON system.query_log, SELECT ON system.disks, SELECT ON system.storage_policies,
    SELECT ON system.data_skipping_indices, SELECT ON system.clusters, SELECT ON system.replicas,
    SELECT ON system.replication_queue TO flare_app;
GRANT CREATE TEMPORARY TABLE ON *.* TO flare_app;
GRANT WRITE ON S3 TO flare_app;
-- Cluster mode only, and with ON CLUSTER 'flare_cluster' on every statement above:
-- GRANT SELECT ON clickhousedb.pipeline_rules_local TO flare_ingest; (and the other five config tables' _local)
-- GRANT CLUSTER ON *.* TO flare_app;
-- GRANT READ ON REMOTE TO flare_app;
```

Then give the services the connection strings. With Helm, put them in a
Secret and set `clickhouse.external.usersSecret`:

```bash
kubectl create secret generic flare-clickhouse-users \
  --from-literal=clickhousedb-ingest='Host=ch.example;Port=8123;Database=clickhousedb;Username=flare_ingest;Password=...' \
  --from-literal=clickhousedb-query='Host=ch.example;Port=8123;Database=clickhousedb;Username=flare_query;Password=...' \
  --from-literal=clickhousedb-app='Host=ch.example;Port=8123;Database=clickhousedb;Username=flare_app;Password=...'
helm upgrade flare deploy/helm/flare --reuse-values --set clickhouse.external.usersSecret=flare-clickhouse-users
```

Elsewhere, set `ConnectionStrings__clickhousedb-ingest`,
`ConnectionStrings__clickhousedb-query` and `ConnectionStrings__clickhousedb-app`
as in step 3 above.

## Check that it worked

Use some pages of the dashboard and send some telemetry. Then, as `default`:

```sql
SELECT user, count() AS queries, countIf(exception_code = 497) AS denied
FROM system.query_log
WHERE event_time > now() - INTERVAL 10 MINUTE AND type != 'QueryStart'
GROUP BY user;
```

All three service users should appear, `default` should only show the
migration queries from startup, and `denied` should be `0`. An
`ACCESS_DENIED` (code 497) in the `ingest`, `api` or `alert-worker` logs
names the missing grant.

## Roll back

Remove the `ConnectionStrings__clickhousedb-*` variables (Helm:
`clickhouse.users.enabled=false`, or an empty `clickhouse.external.usersSecret`)
and restart the services. Each falls back to `ConnectionStrings__clickhousedb`.

## Cap ClickHouse query cost per account

Limit how many rows and how much query time each Flare account, personal
access token or demo visitor can use on `flare_query` per hour. By default
the quota has no limits and only counts usage (see
[the `flare_query` quota](../../reference/clickhouse-service-users.md#the-flare_query-quota)).
Requests over a limit get `429 Too Many Requests` until the hour is up.

Find typical usage first, so the limits don't block normal work. As `default`:

```sql
SELECT quota_key, queries, read_rows, execution_time
FROM system.quotas_usage
WHERE quota_name = 'flare_query'
ORDER BY read_rows DESC;
```

With auth disabled, every request shares the `flare_query` key, so a limit
caps the whole API, not each person.

### Docker Compose or a `flare` CLI instance

1. Create `zz-flare-query-quota.xml` next to `flare-users.xml`. The name must
   sort after `flare-users.xml`: ClickHouse merges `users.d` files in name
   order, and the later file wins.

   ```xml
   <clickhouse>
       <quotas>
           <flare_query>
               <interval>
                   <read_rows>5000000000</read_rows>
                   <execution_time>1800</execution_time>
               </interval>
           </flare_query>
       </quotas>
   </clickhouse>
   ```

2. Mount it on the `clickhouse` service (each `clickhouse-N` in cluster mode):

   ```yaml
   volumes:
     - ./zz-flare-query-quota.xml:/etc/clickhouse-server/users.d/zz-flare-query-quota.xml:ro
   ```

3. Restart ClickHouse: `docker compose up -d clickhouse` (or `flare stop`,
   then `flare start`).

### Helm with the bundled ClickHouse

```bash
helm upgrade flare deploy/helm/flare --reuse-values \
  --set clickhouse.queryQuota.readRows=5000000000 \
  --set clickhouse.queryQuota.executionTimeSeconds=1800
```

### Your own ClickHouse server

```sql
ALTER QUOTA flare_query FOR INTERVAL 1 hour MAX read_rows = 5000000000, execution_time = 1800;
```

### Check the limits

`SELECT quota_key, read_rows, max_read_rows, execution_time, max_execution_time
FROM system.quotas_usage WHERE quota_name = 'flare_query'` shows the new
`max_*` values. When a key runs over, Flare.Api logs
`ClickHouse query quota exceeded for user:…`.

## Add row policies as a second check on project scoping

Make ClickHouse itself hide the rows of services outside a user's
[projects](organize-teams-with-projects.md), so a Flare query that forgot
the project filter still shows nothing it shouldn't. This needs the service
users above. For what the policies cover and what they don't, see
[Row policies on `flare_query`](../../reference/clickhouse-service-users.md#row-policies-on-flare_query).

Turn on both halves together. With the policies mounted and Flare.Api not
sending the scope, nobody sees any telemetry. That includes Flare.Api images
from 0.7.0 or earlier, which never send it.

### Docker Compose or a `flare` CLI instance

1. Mount the policies on the `clickhouse` service (each `clickhouse-N` in
   cluster mode):

   ```yaml
   volumes:
     # Cluster mode: ./db/clickhouse-cluster/config/flare-row-policies.xml
     - ./db/clickhouse/config/flare-row-policies.xml:/etc/clickhouse-server/users.d/flare-row-policies.xml:ro
   ```

2. On the `api` service, add:

   ```yaml
   environment:
     Query__RowPolicyScope: "true"
   ```

3. Recreate the stack: `docker compose up -d`.

### Helm

```bash
helm upgrade flare deploy/helm/flare --reuse-values --set clickhouse.rowPolicies.enabled=true
```

With the bundled ClickHouse, this mounts the policies and restarts
ClickHouse. With `clickhouse.enabled=false`, it only sets
`Query__RowPolicyScope` on api. Mount `flare-row-policies.xml` (the
cluster file for a cluster) in your server's `users.d` first.

### Check the policies

As `default`, `SELECT short_name, table FROM system.row_policies` should list
19 policies on `flare_query`. Then sign in as a project member: the Logs,
Traces and Metrics pages should show the project's services. If every page is
empty for everyone, Flare.Api isn't sending the scope: check
`Query__RowPolicyScope` and the image version.

To turn the policies off, remove the mount (Helm: `clickhouse.rowPolicies.enabled=false`),
restart ClickHouse, and then remove `Query__RowPolicyScope`.
