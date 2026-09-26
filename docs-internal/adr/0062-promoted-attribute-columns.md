# ADR-0062: Promoted attribute columns

Status: Accepted

Date: 2026-09-26

## Context

Every log attribute filter compiles to a `Map` lookup such as
`LogAttributes['http.route'] = {v}`. ClickHouse stores a `Map` as two parallel
arrays, so that lookup reads every key and value of every row in the scanned
granules. The only skip indexes that help are the table-wide
`mapKeys`/`mapValues` bloom filters from `0001_logs.sql`. They prune a granule
only when no row in it has the key or value anywhere in the map, and for a
common key like `http.route` that almost never happens.

The usual ClickHouse answer is a `MATERIALIZED` column per hot key plus its own
skip index
([signoz#6646](https://github.com/SigNoz/signoz/commit/67e822e23ef5744618b5a7d9e516c3c30a35e6c3)).
Which keys are hot depends on the deployment, so this can't be a fixed
migration. An admin needs to add and remove these columns at runtime. Five
things needed a decision:

1. Where the list of promoted keys lives.
2. Column naming.
3. Backfill.
4. Cluster-mode DDL.
5. Demotion, and how the query builders pick up changes.

## Decision

**An Admin action on the Indexing page runs `ALTER TABLE logs ADD COLUMN
attr_<bag>_<key> String MATERIALIZED <Bag>['<key>']` plus a `bloom_filter(0.01)`
skip index on that column. The table's own schema (`system.columns`) is the
registry. `LogFilterSqlBuilder` reads the column instead of the map lookup
wherever that gives the same result.**

- **The registry is `system.columns`, not a new table.** A promoted column is
  any `MATERIALIZED` column on `logs` whose name starts with `attr_` and whose
  `default_expression` parses back to exactly `LogAttributes['k']`,
  `ResourceAttributes['k']` or `ScopeAttributes['k']`. A separate registry
  table could drift from the DDL that actually ran, for example after a
  half-finished promote or a manual `DROP COLUMN`. Reading the schema can't
  drift. `PromotedAttributeRegistry` caches the result as an immutable
  snapshot. `PromotedAttributeRefreshWorker` refreshes it at startup and every
  30 s in both Flare.Api and Flare.AlertWorker, since alert conditions are
  `LogFilter`s too. The query path never makes a round trip for it.
- **Naming: `attr_{log|res|scope}_{key}`**, with every character outside
  `[A-Za-z0-9_]` replaced by `_`. For example, `http.route` becomes
  `attr_log_http_route`. The index is `idx_<column>`. The mapping is not
  injective (`http.route` and `http_route` produce the same name), so the
  promote endpoint returns 409 when a different key already holds the name.
- **Keys are limited to `[A-Za-z0-9_.\-:/@]`, up to 200 characters.** DDL can't
  take bound parameters, so the key is spliced into the `ALTER` as a
  single-quoted literal. With no quote or backslash allowed, that literal needs
  no escaping. Every OTel semantic-convention key fits.
- **Only the operators that give identical results switch to the column.** The
  column holds `Map[key]`, which is `''` for a missing key, so it can't tell
  "absent" from "present but empty".
  - `Equals` always switches to the column. It never had a `mapContains` guard.
  - `NotEquals`, `In` and `NotIn` switch only when no compared value is `''`.
    In that case `col = v` already implies the key is present.
  - `Exists`, `Absent`, `Regex` and `NotRegex` keep the guarded map form.
    A regex can match `''`.

  So promotion never changes which rows match, only how fast they're found.
- **Backfill is optional and runs asynchronously.** With it on (the default),
  the promote runs `MATERIALIZE COLUMN` and `MATERIALIZE INDEX` mutations.
  Without it, only new parts, and old parts merged afterwards, store the
  column. Results are correct either way, because ClickHouse computes a
  missing `MATERIALIZED` column from its expression when reading an older
  part. The Indexing page shows "Backfilling" while a matching mutation in
  `system.mutations` is unfinished. Turning backfill off is for a large table
  whose old data expires by TTL soon anyway.
- **Cluster mode uses the same two-table shape as
  `db/clickhouse-cluster/0010_logs_pattern.sql`.** Promote first alters
  `logs_local ON CLUSTER` (column + index), then adds the same
  `MATERIALIZED` column to the `logs` Distributed table, so the Distributed
  table never exposes a column its shards don't have. Mutations run against
  `logs_local`. Demote reverses the order: Distributed column, then the local
  index, then the local column.
- **Demotion drops the index, then the column.** The admin service removes the
  column from its own snapshot before running the DDL, so that instance never
  builds SQL against a column that's being dropped. The dropped column's data
  is simply gone; nothing else ever wrote to it.
- **Capped at 50 promoted keys.** Each one adds a column and an index to every
  insert.
- **Admin-only** (`adminRoutes`), because it's schema DDL on a shared table.
  Listing is open to any authenticated user.

## Consequences

- No migration. The `attr_` column names are reserved for this feature from
  now on, so a future schema migration must not use that prefix.
- Scope is the `logs` table only. Span attribute filters (`SpanFilterSqlBuilder`)
  still read `SpanAttributes`. Extending this to spans is the same design
  against `spans`/`spans_local`.
- Group-by and value-autocomplete on a promoted key still read the map. Only
  filters use the column.
- After a demote on one Flare.Api instance, other instances and
  Flare.AlertWorker can still reference the dropped column until their next
  refresh, up to 30 s. Queries they run in that window fail with a
  missing-column error and succeed again on the next refresh. A promote has no
  such window: stale instances just keep using the map form.
- Cluster-mode DDL follows the migration pattern above but was verified live
  only on a single node. Single-node was checked end to end: promote, every
  operator's result set compared against the map form, `EXPLAIN indexes=1`
  showing `idx_attr_log_http_route` in use, ingest continuing after promotion,
  and demote.
