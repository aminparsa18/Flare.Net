# ADR-0063: Promoted span attribute columns

Status: Accepted

Date: 2026-09-26

Extends [ADR-0062](0062-promoted-attribute-columns.md), which promoted attribute
keys on the `logs` table only.

## Context

ADR-0062 lets an admin promote a log attribute key to a `MATERIALIZED` column
plus a `bloom_filter` skip index. Span attribute filters have the same problem:
`SpanFilterSqlBuilder` compiles every one to a `SpanAttributes['k']` or
`ResourceAttributes['k']` map lookup, and `spans` has only the table-wide
`mapKeys`/`mapValues` bloom filters from `0007_spans.sql`. ADR-0062 noted that
extending to spans would be "the same design against `spans`/`spans_local`".
Three things still needed a decision:

1. How the API and registry tell the two tables apart.
2. Whether the 50-key cap is global or per table.
3. Whether the `spans_by_start_time` projection (migration 0025) gets in the way.

## Decision

**Everything in ADR-0062 applies to `spans` unchanged. A `PromotedAttributeTable`
(`Logs` | `Spans`) field picks the table.**

- **The table is a new field, not new bag values.** `PromoteAttributeRequest`
  and `PromotedAttributeInfo` gain `Table`, last in the record and defaulting
  to `Logs`, so a pre-ADR-0063 JSON body keeps its meaning. The bag stays
  `AttributeBag`, where `Log` means "the table's own attribute map":
  `LogAttributes` on `logs`, `SpanAttributes` on `spans`. That reuses the enum
  the dashboard and MemoryPack already know, and avoids a parallel
  promotion-only bag enum. `SpanFilterSqlBuilder` maps `SpanAttributeBag.Span`
  to `AttributeBag.Log` when it looks a key up.
- **Naming: `attr_span_{key}`** for the span map, and the same
  `attr_res_`/`attr_scope_` prefixes as logs for the other bags. The same name
  can exist on both tables, for example `attr_res_k8s_namespace_name`, so
  `DELETE /api/indexing/promoted-attributes/{column}` takes `?table=spans`.
  Omitting it means `logs`, as before.
- **One registry, two snapshots.** `PromotedAttributeRegistry` reads
  `system.columns` for `table IN ('logs', 'spans')` in one query and exposes
  `Logs` and `Spans`. The expression parser only accepts a table's own map, so
  a `SpanAttributes['k']` column on `logs` is ignored, not misread.
- **The cap is 50 per table.** Each promoted column costs every insert into
  its own table, not the other one.
- **The projection doesn't interfere.** `spans_by_start_time` stores only the
  columns the Services-tab edges query reads. That query doesn't go through
  `SpanFilterSqlBuilder`, so no query that could use the projection gains a
  promoted-column predicate. Checked live: `MATERIALIZE COLUMN` and
  `MATERIALIZE INDEX` mutations on `spans` finish normally with the projection
  present, and the projection's parts stay intact.

## Consequences

- No migration. The `attr_` prefix is now reserved on `spans` as well.
- Only the span search and span attribute-values (facet) queries read promoted
  columns, since they're the only callers of `SpanFilterSqlBuilder`.
  Exception, service and messaging queries build their own `WHERE` clauses and
  still read the maps.
- Flare.AlertWorker refreshes both snapshots but reads only `Logs`. No alert
  condition is a `SpanFilter` today.
- Verified end to end on a single node: promote on `spans` with backfill,
  every operator's result count identical before and after, `EXPLAIN
  indexes=1` showing `idx_attr_span_http_route` in use, `system.query_log`
  showing the API querying the column, ingest continuing, and demote by
  `?table=spans` leaving a same-named `logs` column alone. Cluster-mode DDL
  follows ADR-0062's two-table order against `spans_local` and `spans`. Like
  ADR-0062, it was checked only in unit tests, not on a live cluster.
