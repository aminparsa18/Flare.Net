# ADR-0218: Span kind and negated name/status in the structure condition

Status: Accepted

Date: 2026-10-14

Amends [ADR-0217](0217-tempo-compatible-trace-api.md), which rejected `kind`, `name` with anything but `=`,
and `status !=` because the structure condition could not express them.

## Decision

`TraceSpanCondition` (the per-span condition of a structural trace query, ADR-0069) gains four members,
appended last so MemoryPack stays wire-compatible with older clients:

- `SpanNameMatch` (`Equals` default, `NotEquals`, `Regex`, `NotRegex`) says how `SpanName` is compared.
  Regex is ClickHouse `match()` (RE2), unanchored, like the attribute filters; the Tempo planner anchors
  the pattern (`^(?:...)$`) as Tempo does.
- `StatusCodeNegated` turns `StatusCode = x` into `StatusCode != x`.
- `Kind` (OTel `SpanKind` number, 0-5, same encoding as `SpanFilter.Kinds`) and `KindNegated`.

`TraceQlParser` already parsed the kind words; `TempoSearchPlanner` now maps `kind = server`, `kind != client`,
`name != "x"`, `name =~ "x.*"`, `name !~ "x"` and `status != error` onto them. A condition with only a kind
counts as something to match on. Comparing the same field twice in one spanset is still an error unless both
comparisons are identical.

## Consequences

- The Traces page structure builder does not expose the new members yet; the dashboard's hand-written
  MemoryPack encoder leaves them at their defaults, which means "as before".
- `status != error` matches spans that have any other status, including unset. TraceQL's `kind != x` and
  `name != x` match every other span too, which is what the SQL says.
- Still rejected: `statusMessage`, comparing two fields, pipelines, and the other items in ADR-0217.
