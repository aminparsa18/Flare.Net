# ADR-0219: Matching spans (`spanSets`) in Tempo search results

Status: Accepted

Date: 2026-10-14

Amends [ADR-0217](0217-tempo-compatible-trace-api.md), which returned traces without the spans that matched.

## Decision

- **`/tempo/api/search` adds `spanSets` and `spanSet`** to each trace when the query constrains spans (it has a
  structure; `{ }` and trace-level-only queries have none). Each holds up to `spss` spans (default 3, at most 100,
  as in Tempo) with `spanID`, `name`, `startTimeUnixNano`, `durationNanos` (both strings, as Tempo writes uint64)
  and a `service.name` attribute, plus `matched`, the trace's total. `spanSet` repeats the first, Tempo's
  deprecated singular form that Grafana still reads.
- **A second query, after the trace search,** `TraceMatchedSpansQueryBuilder` via
  `ISpanQueryService.GetMatchedSpansAsync`: the spans of the returned trace ids matching any of the plan's
  conditions, using the structure builder's own per-condition predicate (`TraceStructureSqlBuilder.ConditionMatch`)
  so the two cannot disagree about what matches. `count() OVER (PARTITION BY TraceId)` supplies `matched` and
  `LIMIT n BY TraceId` keeps the first spans by start time. It is by `TraceId`, the first sort-key column, with no
  time window (a trace's spans can start outside the window of its root), and it applies the service scope.
- **One group per trace,** not per TraceQL spanset. In `{ a } >> { b }` the spans of `a` and `b` are listed together.
  Tempo returns one group per spanset; Grafana renders either.
- **A regular expression ClickHouse cannot compile is a 400** rather than a 500 (`CANNOT_COMPILE_REGEXP`). The
  planner does not pre-validate: RE2 and .NET syntax differ, and the server is the authority.

Verified against a real ClickHouse: `{ kind = internal && name =~ "handle.*" && status != error }` and a
`{ ... } >> { ... }` search return the matching spans, and a malformed pattern returns 400.

## Consequences

- Grafana's TraceQL results table can expand a trace into the spans that matched.
- Search costs one more ClickHouse query when the query is span-specific, bounded by the page size (at most 200
  traces) and `spss`.
- Only `service.name` is attached to a listed span; the attributes the query compared are not echoed back.
