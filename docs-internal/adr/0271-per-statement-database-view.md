# ADR-0271: Per-statement database view

Status: Accepted

Date: 2026-10-22

## Context

The roadmap asked for a database view ranking normalized statements by rate and p95, built from the `db.*` span attributes, and said to check first whether N+1 detection and span-duration comparison already covered it.

They don't. N+1 ([find-n-plus-one-queries](../../docs/how-to/investigate/find-n-plus-one-queries.md)) only reports statements that one parent span repeated inside a single trace, ranked by total time, with no rate, error count or percentile. Span-duration comparison ranks one span against similar spans and says nothing about which statements cost the most overall. The Services Map drill-down groups database calls by system and operation only (`SELECT`, `GET`), so two different `SELECT`s are indistinguishable. A statement that is slow but never repeated, or called once per request across thousands of requests, appeared nowhere.

## Decision

- **A new tab and endpoint, `POST /api/traces/db-statements`,** next to N+1, computed from `spans` at query time. No migration and no materialized view. Same shape as N+1: a pure `DbStatementQueryBuilder`, a thin `DbStatementQueryService`, plain JSON on the wire.
- **Same statement text as N+1.** The builder reuses `NPlusOneQueryBuilder.StatementExpr`, so a statement is the same string on both tabs and a user can move between them. Literals become `?`; with no query text it falls back to `operation collection`.
- **Rows are keyed by `(ServiceName, DbSystem, Statement)`.** `DbSystem` is `db.system.name` else `db.system`, matching the Map drill-down. Both attribute generations land in one row.
- **Root database spans count.** N+1 ignores them because they cannot repeat under a parent. A rate and p95 view must include every execution.
- **Weighted by `SampleWeight`.** Calls, errors, total time and p95 (`quantileTDigestWeighted`) follow ingest-time trace sampling ([ADR-0122](0122-ingest-trace-sampling.md)) the same way the Map drill-down does, so a sampled service does not look quieter than it is.
- **Sort is a whitelist.** `totalTime` (default), `p95`, `calls` or `errors`, each mapped to a SQL alias server-side. The request value is never interpolated. Up to 100 rows, 5 to 1440 minute window (default 60), optional service and database-system filters, and the ambient `ServiceScope` applies like every other span query.
- **The example trace is the slowest execution** (`argMax(TraceId, DurationNano)`), so the link lands on the worst case.

## Consequences

- No extra storage and nothing to backfill; the view also covers data stored before it existed.
- The query scans the window's database spans and runs a regexp over each statement. It is capped like every other query (`QuerySafety.Full`) and the window tops out at 24 hours. If that proves too slow on large installations, the next step is a pre-aggregated table keyed by the same expression, as ADR-0031 did for the service map; none is built now.
- Statement text is normalized by regexp, so unparameterized SQL with inlined identifiers or long `IN (...)` lists can still produce many distinct rows. Parameterized ORM output is already stable.
- Not built: per-statement time series, a comparison against the previous window, and CLI or MCP exposure.
