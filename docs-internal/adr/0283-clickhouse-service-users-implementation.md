# ADR-0283: What implementing the ClickHouse service users changed

Status: Accepted

Date: 2026-11-27

Supersedes four details of [ADR-0282](0282-clickhouse-service-users.md): "none of them gets `SOURCES`", the claim that
`flare_query` can read `system.*`, "every query service is on the `query` client", and the single users file. The rest
of ADR-0282 stands.

## Context

ADR-0282 asked the implementation to confirm grant names against the pinned image (`clickhouse/clickhouse-server:26.8`).
Probing that image and the code paths, and a live run as the split users, turned up five things ADR-0282 didn't
account for:

- **The image requires grants for `system.*`.** Its own `config.xml` sets `select_from_system_db_requires_grant` to
  `true`. Only `system.tables`, `system.columns`, `system.databases` and the constant tables stay readable without a
  grant. ADR-0282 assumed the server default (off).
- **Some `flare_app` work needs table functions.** AlertWorker's archive export runs `INSERT INTO FUNCTION s3(...)`.
  ClickHouse checks that as `WRITE ON S3` plus `CREATE TEMPORARY TABLE`. In cluster mode, erasure, the Indexing page and
  cluster status read through `clusterAllReplicas()`, which ClickHouse checks as `READ ON REMOTE`.
- **The driver's insert path needs SELECT.** Without explicit column types, `InsertBinaryAsync` learns them by running
  `SELECT cols FROM table WHERE 1=0`. With INSERT alone that's `ACCESS_DENIED`.
- **The materialized views on `spans` check the inserting user.** Migrations 0022, 0023, 0035, 0047 and 0049 created
  them without an `SQL SECURITY` clause, so a `flare_ingest` insert into `spans` fails inside every view with
  `ACCESS_DENIED` (no SELECT on `spans`). `ClickHouseSpanWriter` sets `materialized_views_ignore_errors`, so the span
  insert itself succeeded and the rollups (Services RED metrics, the dependency map, outbound calls, LLM calls, SLO
  minutes) silently stayed empty. Only the live run showed it; no unit test could.
- **An unset password variable leaves an empty password.** ClickHouse logs `Env variable is not set` and loads the user
  with an empty password.

## Decision

- **In cluster mode `flare_ingest` also reads the config tables' `_local` tables.** A read through a Distributed
  table runs on the shards as the same user, so the six config tables need SELECT on both names. The live cluster
  run showed the gap as `ACCESS_DENIED` on `log_metrics_local` and `metric_attribute_rules_local`.
- **`flare_app` gets narrow source grants:** `WRITE ON S3` and `CREATE TEMPORARY TABLE` everywhere, plus
  `READ ON REMOTE` and `CLUSTER` in cluster mode only. It can't read from S3, and it can't use `url()`, `file()` or any
  other source. `flare_ingest` and `flare_query` still get no source grants at all.
- **`flare_app` gets SELECT on the system tables Flare reads,** namely `tables` (for `cluster()` reads), `mutations`, `parts`, `part_log`, `query_log`,
  `disks`, `storage_policies`, `data_skipping_indices`, `clusters`, `replicas` and `replication_queue`. `flare_query`
  gets none.
- **`IndexingQueryService` and `ClusterQueryService` run as `flare_app`, not `flare_query`.** Their SQL is fixed rather
  than built from request input. They need the system grants above and, in cluster mode, `READ ON REMOTE`. Keeping
  them on `flare_query` would have meant giving those grants to the user that runs generated SQL.
- **Ingest reads its insert column types from `system.columns`.** ClickHouse lists a table's columns to any user with a
  privilege on that table. `ClickHouseInsertColumnTypes` caches the types per table and passes them as
  `InsertOptions.ColumnTypes`. So `flare_ingest` keeps INSERT without SELECT, and still can't read telemetry.
- **Materialized views run as their definer.** Migration 0092 runs
  `ALTER TABLE ... MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER` on all seven views, so they run as the
  migration user and `flare_ingest` still needs no SELECT on `spans`. Every new materialized view must say
  `SQL SECURITY DEFINER` in its own `CREATE`.
- **There are two users files plus one copy,** because cluster mode adds grants (two for `flare_app`, six `_local` SELECTs for `flare_ingest`): `db/clickhouse/config/flare-users.xml`,
  `db/clickhouse-cluster/config/flare-users.xml`, and Helm's `files/flare-users.xml`, which must equal the single-node
  file because a chart can't read files outside itself. `FlareClickHouseUsersTests` fails if the cluster file differs
  in anything but those cluster grants, or if the chart copy differs at all.
- **No install path leaves a password variable unset.** Compose defaults each to `flare`, like `CLICKHOUSE_PASSWORD`.
  `install.sh`, the CLI and Helm generate them. The AppHost uses secret parameters. The users file and the docs warn
  about the empty-password behaviour for anyone mounting the file by hand.
- **AlertWorker keeps `clickhousedb` wherever it runs a published image** (Helm and the CLI's Compose templates).
  It runs no migrations and only needs `clickhousedb-app`, but AlertWorker images up to 0.6.0 read only
  `clickhousedb` and crash-loop without it. Both paths can pair the new template with an older image: Helm's
  `appVersion`, a pinned `image.tag` or `FLARE_IMAGE_TAG`, or a chart upgraded before its images. Newer images prefer
  `clickhousedb-app` and still connect as `flare_app`. The root Compose files and the AppHost build AlertWorker from
  source, so they leave `clickhousedb` out.

## Alternatives considered

- **Giving `flare_ingest` SELECT on the data tables** to satisfy the driver. Rejected: it's the "Ingest can't read
  telemetry" property ADR-0282 is paying for.
- **Giving `flare_ingest` SELECT on `spans` for the views.** Rejected for the same reason.
- **Hard-coding column types per writer.** Rejected: it duplicates the schema in C#, and drifts silently when a
  migration changes a type.
- **Turning off `select_from_system_db_requires_grant` in a `config.d` file.** Rejected: it makes every `system.*` table,
  `query_log` included, readable to `flare_query`.
- **Archiving through the S3 SDK instead of `s3()`.** That would remove the S3 grant, but it streams every exported row
  through AlertWorker. It's deferred; the grant is write-only.

## Consequences

- A new system table read by Flare.Api, or a new table Ingest reads, means editing both users files and the chart copy.
  ClickHouse refuses to start on a malformed grant, so a typo shows up at the next start, not later.
- A new materialized view without `SQL SECURITY DEFINER` loses its rows silently under `flare_ingest`, because of
  `materialized_views_ignore_errors`. Review new views for the clause.
- `MODIFY SQL SECURITY` needs ClickHouse 24.4 or later. That's fine for the pinned image; an older external server
  fails migration 0092 at startup.
- Raising `Query__MaxExecutionSeconds` above 300, or `Query__MaxRowsToRead` above 10,000,000,000, also means raising the
  `flare_query` ceilings in the users file (ADR-0282).
- Under Helm and the CLI, the `default` password sits in AlertWorker's environment though it never uses it. Removing
  it from those templates is safe once no supported image is 0.6.0 or older.

## Related documentation

- [Reference: ClickHouse service users](../../docs/reference/clickhouse-service-users.md)
- [How-to: move an existing install to the ClickHouse service users](../../docs/how-to/admin/use-clickhouse-service-users.md)
