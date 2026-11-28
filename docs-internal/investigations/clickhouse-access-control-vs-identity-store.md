# Investigation: can ClickHouse access control replace the identity store, and what else can it do for Flare?

Date: 2026-11-27
Environment: `clickhouse/clickhouse-server:26.8` (26.8.24.9, the pinned image), throwaway containers.
Related: ADR-0004, ADR-0009, ADR-0074, ADR-0111, ADR-0123, and
[ADR-0282](../adr/0282-clickhouse-service-users.md), which came out of this investigation.

The question was whether ClickHouse's users, roles, grants, row policies, profiles and quotas could replace
`Flare.Identity` (and so remove Postgres from cluster mode), and if not, how they could improve Flare. Short answer:
they can't replace it, in any form tried. Least-privilege service users are worth doing, and cluster mode can drop
Postgres another way. No production behaviour changed.

## Facts established from the code

- **The upserts.** 14 of the 21 `Db*Store` classes use `ON CONFLICT DO UPDATE`. Only the five auth-settings stores add
  `RETURNING`. `DbDashboardPinStore` uses `DO NOTHING`, and `DbLlmModelPriceStore` deletes and then inserts.
- **Token redemption.** `DbPasswordSetTokenStore.ConsumeAsync` runs `DELETE ... RETURNING`. Issuing a token uses
  `INSERT ... SELECT ... WHERE NOT EXISTS`.
- **The audit chain** relies on three things: a `BIGINT GENERATED ALWAYS AS IDENTITY` id (the head is
  `ORDER BY Id DESC LIMIT 1`), `pg_advisory_xact_lock` plus a process semaphore, and the `TR_AuditEvents_NoUpdate`
  trigger.
- **Unique constraints.** `UX_Users_Username` is on `LOWER(Username)`, `UX_Users_AuthProvider_ExternalId` on
  `(AuthProvider, ExternalId)`, and `UX_Projects_Name` on `LOWER(Name)`. `LoginAttempts` is a read-then-upsert counter.
- **Session activity.** `SessionAuthenticationHandler` updates `Sessions.LastSeenAt` at least once a minute for every
  active session.
- **What Ingest needs from Identity.** Flare.Ingest uses only `IIngestApiKeyStore`, and only to read it: a 30-second
  poll with last-known-good fallback in `IngestApiKeyCache`. Per-key usage lives in Redis (`RedisIngestKeyUsageStore`).
  Ingest also runs the identity migrations. Flare.AlertWorker doesn't reference `Flare.Identity`.
- **Api replicas.** Api runs one replica everywhere (`docker-compose.cluster.yml`, and Helm's `api.replicas: 1`).
  ADR-0111 says running several isn't supported yet. So in cluster mode the only reader of the identity store on
  another host is Ingest.
- **Config in ClickHouse.** About 23 config tables read through `LatestVersionSql`, not just alert rules, dashboards
  and saved views. ADR-0009 explains why: query-domain config lives next to the telemetry it filters. The line is
  blurry in places. `ApdexThresholds`, `MetricMetadataOverrides`, `SourceLinks`, `IssueTrackers`, `SourceMaps`,
  `LlmModelPrices`, `UserPreferences` and `DashboardPins` sit in Identity as "per-installation config", while
  `runtime_settings` and `metric_attribute_rules` have the same shape and sit in ClickHouse.
- **One ClickHouse credential everywhere.** Every install path connects as the image's `default` user:
  `docker-compose*.yml`, the CLI templates, Helm (`flare.clickhouseUser`, which is `default` for the bundled ClickHouse),
  and Aspire (`AddClickHouse`).
- **Privileges each service needs:**
  - Ingest: INSERT on the data tables; SELECT on 6 config tables (`pipeline_rules`, `log_metrics`,
    `metric_attribute_rules`, `runtime_settings`, `telemetry_exports`, `checkin_monitors`); DDL at startup for
    `ClickHouseMigrationRunner`.
  - Api: SELECT; config writes; runtime DDL (retention `MODIFY TTL`, `MODIFY COLUMN` and `MODIFY SETTING storage_policy`,
    promoted-column `ADD COLUMN`/`MATERIALIZE`, erasure `ALTER ... DELETE`, with `ON CLUSTER` in cluster mode); the same
    startup DDL.
  - AlertWorker: SELECT through Api's query services, plus INSERT into `metrics_gauge`.
  - The Grafana plugin only calls Flare.Api.
- **No user-written SQL.** No endpoint passes SQL from a user straight through to ClickHouse. Every query is generated
  by Flare.

## Probes on 26.8

### `access_management` is off for Flare's `default` user

`/entrypoint.sh` line 52 reads `CLICKHOUSE_ACCESS_MANAGEMENT="${CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT:-0}"`, and Flare
sets that variable nowhere. So Flare can't run `CREATE USER` today without changing how every ClickHouse container is
configured.

### A row policy driven by a custom setting works

```sql
CREATE SETTINGS PROFILE flare_query SETTINGS SQL_flare_scope = '', max_execution_time = 30 MAX 60, ...;
CREATE ROW POLICY scope ON clickhousedb.logs AS RESTRICTIVE FOR SELECT
  USING getSetting('SQL_flare_scope') = '' OR arrayExists(p -> if(endsWith(p,'*'),
        startsWith(ServiceName, substring(p,1,length(p)-1)), ServiceName = p),
        splitByChar(',', getSetting('SQL_flare_scope'))) TO flare_api;
```

Results:

- With no scope set, the user saw `['billing','checkout-api','checkout-web']`.
- With `SETTINGS SQL_flare_scope='checkout-*'`, it saw `['checkout-api','checkout-web']`.

### Settings constraints reject values over the limit

`SETTINGS max_execution_time=600` against `MAX 60` failed with
`Code: 452 ... Setting max_execution_time shouldn't be greater than 60. (SETTING_CONSTRAINT_VIOLATION)`.

### Grants are checked per column

- An INSERT by a SELECT-only user failed with `ACCESS_DENIED`, naming `INSERT(Timestamp, ServiceName, Body)`.
- A SELECT by an INSERT-only user failed with `ACCESS_DENIED`.
- `ALTER ... ADD COLUMN` without the grant failed with `ACCESS_DENIED`, naming `ALTER ADD COLUMN(z)`.

### Users can be declared in XML with passwords from the environment

The probe mounted a `users.d/flare-users.xml` that declares each user with `<password from_env="..."/>` and
`<grants><query>GRANT ...</query></grants>`. ClickHouse read it at server start, without `access_management`.

| User | Statement | Result |
|---|---|---|
| `flare_ingest` (INSERT on `clickhousedb.*`, SELECT on `pipeline_rules`) | `INSERT INTO logs` | succeeded |
| | `SELECT FROM pipeline_rules` | succeeded |
| | `SELECT FROM logs` | `ACCESS_DENIED` |
| | `url('http://example.com', ...)` | `ACCESS_DENIED` |
| `flare_query` (SELECT only, `readonly=2`, `max_execution_time` capped at 120) | `max_execution_time=60` | succeeded |
| | `max_execution_time=600` | `SETTING_CONSTRAINT_VIOLATION` |
| | `INSERT` | `ACCESS_DENIED` |
| | `file('/etc/passwd', ...)` | `DATABASE_ACCESS_DENIED` |

## What the ClickHouse docs say

- **Row policies** ([row-policy](https://clickhouse.com/docs/sql-reference/statements/create/row-policy)):
  - They aren't applied on a `Distributed` table. They have to be on the local tables, and with
    `serialize_query_plan = 1` reads through `Distributed` come back unfiltered.
  - Materialized views and Buffer tables don't inherit them.
  - `read_rows`, `system.parts` and `EXPLAIN ESTIMATE` still count hidden rows.
- **Constraints and readonly** ([constraints](https://clickhouse.com/docs/operations/settings/constraints-on-settings)):
  - With `readonly=1` the client can't change settings unless `changeable_in_readonly` allows it.
  - `readonly=2` allows `SET`. It also allows `RESTORE`, and `readonly` never restricts `BACKUP`.
- **Quotas** ([quotas](https://clickhouse.com/docs/operations/quotas)):
  - They can be keyed by a `quota_key` the client sends.
  - Usage accumulates on the initiator node, and counters reset when the server restarts.
- **External authenticators**
  ([external-authenticators](https://clickhouse.com/docs/concepts/features/security/external-authenticators/index)):
  - The list is LDAP, Kerberos, SSL X.509, HTTP (Basic over GET only, not in Cloud) and JWT.
  - The JWT page is written for Cloud, and a custom IdP needs the Enterprise plan. Nothing covers browser OIDC sign-in
    for self-hosted installs.
- **`CREATE USER`** ([create user](https://clickhouse.com/docs/reference/statements/create/user)):
  - It supports `VALID UNTIL`, multiple auth methods and bcrypt.
  - `password_complexity` is a server-wide `config.xml` setting.
  - There's no lockout with a duration. The `failed_sequential_authentications` quota counter is the closest thing.
- **Transactions** ([transactional](https://clickhouse.com/docs/guides/developer/transactional)):
  - Experimental.
  - "Non-Replicated MergeTree" only.
  - Need Keeper.
  - Not available in Cloud.
- **Lightweight DELETE** ([delete](https://clickhouse.com/docs/sql-reference/statements/delete)): it's a mutation that
  masks rows, with no `RETURNING` and no compare-and-swap.
- **KeeperMap** ([keeper-map](https://clickhouse.com/docs/engines/table-engines/special/keeper-map)):
  - Writes are linearizable.
  - The primary key is a single column, with no secondary indexes.
  - Strict mode makes an insert of an existing key fail.
  - Compare-and-swap isn't documented.
  - The server needs `keeper_map_path_prefix` configured.

## Conclusions

1. **Using ClickHouse users as Flare users is a dead end.** ClickHouse authenticates connections to ClickHouse, not
   browsers or OTLP senders. It has nothing for these:
   - OIDC/Entra sign-in for self-hosted installs
   - trusted headers from a reverse proxy
   - sessions with an idle timeout
   - lockout per (username, IP)
   - per-key ingest limits, allowed origins or allowed services

   Creating users at runtime would also need `access_management` on Api's credential. An injection in the generated SQL
   could then create persistent backdoor users.
2. **Storing identity in ClickHouse tables is a dead end.** These break:
   - Password-set tokens could be redeemed twice, because there's no atomic delete-and-return.
   - Parallel failed logins lose counter updates, which bypasses the lockout.
   - Two concurrent just-in-time logins can create duplicate users, because there are no unique constraints.
   - A revoked session can still be accepted, because replicas inside a shard lag and `insert_distributed_sync` only
     covers fan-out between shards.
   - Session `LastSeenAt` writes churn row versions.
   - The audit chain loses its identity id, its serialized append and its no-update trigger.

   The config-table pattern works because those tables are edited by admins, last writer wins, they are keyed by GUIDs
   Flare generates, and nothing in them is single-use or unique.
3. **KeeperMap is a dead end.** Every secondary lookup (by username, by user id, by token hash) would need index keys
   kept by hand, with no transactions across keys. It would be a third provider that exists only in cluster mode.
4. **Cluster mode can drop Postgres without moving identity.** Api is the single owner of SQLite and publishes the
   ingest-key set to a versioned ClickHouse table that Ingest polls. Ingest already tolerates 30 seconds of staleness,
   so this adds no new correctness risk. This is on the roadmap.
5. **Least-privilege ClickHouse users are worth doing.** XML users with passwords from the environment work on existing
   volumes and in cluster mode without replicated access storage. Decided in ADR-0282.
6. **Row policies would only be a second line of defence.** The client sets the custom setting, so the policy protects
   against a builder that forgets the filter, not against a compromised Api. It would have to sit on every `_local` and
   aggregate table. If built, make it opt-in and fail closed. Done in
   [ADR-0287](../adr/0287-opt-in-row-policies-on-flare-query.md).
