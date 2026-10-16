# ADR-0234: A combination of spansets as the selection of `compare()`

Status: Accepted

Date: 2026-10-16

Extends [ADR-0226](0226-traces-drilldown-compatibility-and-compare.md), which took one spanset as the selection, and
[ADR-0232](0232-traceql-metrics-spanset-combinations.md), whose `SpanSetExpression` and compiler this reuses. Second part of the fixed list started in
[ADR-0233](0233-traceql-event-name-status-message-filters-and-typed-group-labels.md).

## What Tempo does

Tempo's grammar takes exactly one `spansetFilter` there (`COMPARE OPEN_PARENS spansetFilter ...` in `pkg/traceql/expr.y`), so
`compare({ a } || { b })` is not valid TraceQL. This is therefore an extension: a query that Tempo accepts means the same here, and one it
rejects is accepted with the meaning of the same expression as a metrics query's spansets (ADR-0232).

## Decision

- **The selection is a `SpanSetExpression`.** `SpanCompare.Selection` was a `TraceSpanCondition`; it is now the expression, and the usual
  selection `compare({ status = error })` is a `SpanSetCondition` of it, so nothing changes for it. The builder compiles it with the `SpanSetCompiler`
  of ADR-0232 into one boolean on the span row, which is what `IsSel` always was (`(<predicate>) AS IsSel`). `||`, `&&`, parentheses and the
  structural operators, union ones included, all work as they do in a metrics query's spanset: the spans in the result of the expression are the
  selection and every other span of the query is the baseline.
- **Parsing.** `ParseCompareArguments` reads the first argument with the same `or` expression parser as the query (it accepted one `{ }`
  before). One spanset, also in parentheses, keeps `CompareSelection` alone; anything else also sets `TraceQlMetricsQuery.CompareCombination`
  (spansets, expression, tree, relations), at most six spansets. The relations written inside the selection are lettered against its own spansets,
  so they are taken out of the parser's list before the query's own are read, and the query's union relations stay what they were.
- **Planning.** `TempoMetricsPlanner.SpanSets` (the code that turned a query's combination into an expression, now shared) plans each spanset of
  the selection as a search condition, so value groups, the unscoped-attribute guess and the empty side of a structural operator read the same.
  A spanset with no span condition, an empty `{ }` beside `&&` / `||` and `rootName`, `rootServiceName` or `traceDuration` next to another spanset
  are errors naming the rule. `compare({ })` alone still selects every span.
- **Parameters.** The selection's conditions are lettered from `S` (`S` to `X`) in order of appearance, whatever they are called, so their bound
  parameters (`tsS...`) never meet those of the query's own spansets (`A` to `F`, `P` for an ancestor), which may be called the same.
- **The selection window** (`start`, `end` of `compare()`) is added after the whole expression: `((<expression>) AND StartTime > ... AND StartTime <= ...)`.
  A span qualifies by its own start time; the trace-level "a span in each side" of `&&` is not narrowed to the window, as in Tempo, where the
  expression is evaluated first and the window is checked on the resulting spans.
- **The shape of the response is unchanged**: `selection` / `baseline` series per attribute value, their totals, and the too-many-values marker.

## Verified

Against ClickHouse with 444 seeded spans (60 traces of `tqseed-front` / `tqseed-back`, see ADR-0233), `compare()` over `{ resource.service.name =~ "tqseed.*" }`
gave, for each of the attributes `name`, `kind`, `span.tq.code` and `resource.tq.region`, exactly the `selection` and `baseline` counts of a hand-written query
that marks the selection spans with a `LEFT JOIN`, building the sets with `INNER JOIN` / `INTERSECT` of trace ids rather than `IN` subqueries:

| Selection | Result |
|---|---|
| `{ name = "call-a" }` (unchanged) | all four attributes equal |
| `{ name = "call-a" } \|\| { name = "call-b" }` | equal |
| `{ name = "call-a" } \|\| { kind = server }` (overlapping sides) | equal |
| `{ name = "call-a" } && { name = "log" }` (`INTERSECT` of the traces) | equal |
| `{ name = "call-a" } && { name = "call-b" } && { name = "log" }` | equal |
| `({ name = "call-a" } && { name = "call-b" }) \|\| { name = "cache.get" }` | equal |
| `{ name = "call-a" } !> { name = "handle-b" }` (a relation, an anti-join on the parent id) | equal |

## Consequences

- Traces Drilldown never sends a combination, so nothing it does changes; `compare()` over a combination is for queries written by hand.
- `compare()` over a combination reads the spans of its window twice in the worst case, as a metrics query does (ADR-0232), on top of the two passes
  `compare()` already makes (ADR-0226). The query caps and the 200-step limit apply.
- `SpanCompare` is an internal record; code that built one from a bare condition wraps it in `SpanSetCondition`.
