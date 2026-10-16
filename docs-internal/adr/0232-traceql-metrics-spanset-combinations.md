# ADR-0232: `&&`, `||` and union operators between spansets in a TraceQL metrics query

Status: Accepted

Date: 2026-10-16

Extends [ADR-0225](0225-traceql-metrics-comparison-and-span-relations.md), which allowed one spanset or one relation in a metrics query and
rejected the rest, and [ADR-0231](0231-traceql-sibling-negated-and-union-structural-operators.md). Last part of the fixed list started in
[ADR-0229](0229-traceql-kind-sets-and-colon-intrinsics.md); with it the roadmap's "Tempo API follow-ups" item is done and deleted.

## What Tempo does (`pkg/traceql/ast_execute.go`)

Per trace, with `L` and `R` the spans each side selects:

- `{L} && {R}`: if **both** are non-empty, the **union** of the two sets of spans (each span once); otherwise nothing.
- `{L} || {R}`: the union, whenever either is non-empty.
- The structural operators return their own sets (ADR-0231), and are operands of `&&` and `||` like any spanset.

A metrics function then counts or aggregates those spans, so a span in both sides is counted once.

## Decision

- **The result is a set of spans, compiled into one boolean over a span row.** `SpanSetExpression` is a tree: `SpanSetCondition` (a spanset's
  condition), `SpanSetRelation` (a structural operator over two conditions, with its kind, negation and whether it is a union), `SpanSetAnd`,
  `SpanSetOr`. `SpanMetricsRequest.Spansets` carries it, and when it is set it replaces `Condition`, `Ancestor` and `Root`; the group ranking,
  the exemplar query, `compare()` and every function use the one predicate, so they agree on which spans count.
- **`A || B`** is `(P(A) OR P(B))`.
- **`A && B`** is `((P(A) OR P(B)) AND TraceId IN (traces with a span in each))`, the trace set being
  `SELECT TraceId FROM (SELECT TraceId, P(A) AS L, P(B) AS R FROM spans WHERE <look-back window> AND (L OR R)) GROUP BY TraceId HAVING max(L) > 0 AND max(R) > 0`.
  The two flags are computed in their own `SELECT`, below the `GROUP BY`: ClickHouse 26.8's analyzer loses a column when an `IN (subquery)`
  (a relation inside `P`) sits inside a grouped projection. Nesting repeats a side twice per level (the union and the trace set), so a
  six-spanset query is bounded at 32 copies of its innermost predicate.
- **Relations in a combination** reuse the clauses of ADR-0225 and ADR-0231 (`RelationClause`, now taking the two conditions' predicates),
  so `{a} !>> {b}` inside a combination counts the same spans as alone.
- **Union operators** (`&>>`, `&>`, `&~`), rejected in a metrics query by ADR-0227 and ADR-0231, are now accepted: the counted spans are
  `(P(R) AND related(L→R)) OR (P(L) AND related(R→L))`. The second half is the inverse relation, per kind: a left span that is the *parent*
  of a right span is `(TraceId, SpanId) IN (SELECT TraceId, ParentSpanId ... right)`; one with a right *sibling* is the sibling clause with the
  roles exchanged; one with a right *descendant* is found by the same per-trace walk that collects, for each right span, every ancestor up to 64
  levels (`arrayFold` accumulating the frontier, `arrayDistinct`, `ARRAY JOIN`), compared by `(TraceId, cityHash64(SpanId))`.
- **Parsing.** A query that is one spanset or one plain, non-union relation keeps its old shape (`Ancestor`). Any other `or` expression of the
  grammar is a `TraceQlCombination`: the spansets, the expression the search planner reads, the tree (parsed by the structure expression
  parser, since TraceQL's `&&` / `||` / structural operators map onto it) and the relations written (to know which are unions). At most six
  spansets. `TempoMetricsPlanner` plans every spanset as a search condition (value groups, empty sides of a relation, the unscoped-attribute
  guess all read the same), so a spanset that plans to nothing, or an empty `{ }` beside `&&` / `||`, is an error naming the rule. `rootName`,
  `rootServiceName` and `traceDuration` stay rejected next to another spanset (the planner already says so).
- **Look-back.** The trace-level "has a span in each side" and the relation clauses look at spans from an hour before the window
  (`TraceQlMetricsQueryBuilder.Lookback`), as in ADR-0225.

## Verified

Against ClickHouse on the test table plus 345 seeded spans (60 traces; see ADR-0231), `count_over_time()` equalled hand-written `count()`:

| Query | API | SQL |
|---|---|---|
| `{call-a} \|\| {call-b}` | 91 | 91 |
| `{call-a} \|\| {kind = client}` (overlapping sides, a span counted once) | 131 | 131 |
| `{call-a} && {call-b}` / `&& {log}` / `{call-a} && {call-b} && {log}` | 76 / 56 / 63 | `INTERSECT` of the trace ids: 76 / 56 / 63 |
| `({call-a} && {call-b}) \|\| {cache.get}` | 105 | 105 |
| `{call-a} && ({call-b} && {log})` | 63 | 63 |
| a six-spanset chain of nested `&&` | 126 | 126 |
| `({call-a} ~ {call-b}) \|\| {log}` | 72 | 72 |
| `({call-a} ~ {call-b}) && {log}` | 42 | 42 |
| `{call-b} !>> {db.query} && {log}` | 46 | 46 |
| `{call-a} &> {handle-a}` / `&>> {db.query}` / `&~ {call-b}` | 100 / 80 / 76 | 100 / 80 / 76 (both sides, `JOIN`) |

`quantile_over_time(duration, .5) by (name)` over `{call-a} || {call-b}` returned 0.15 s and 0.1 s, as `quantileTDigestWeighted` over the same rows;
`with(exemplars=true)`, a range query and `compare({ status = error })` run on a combination.

## Not done

- A combination of spansets as the *selection* of `compare()`; it takes one spanset.
- `<`, `<<` and their `!` / `&` forms (ADR-0231).

## Consequences

- Every spanset operator of Tempo's grammar that has a Flare meaning is now accepted in search and in metrics (`<` and `<<` apart).
- A combination reads the spans of its window twice in the worst case (the predicate and the trace set); with relations inside, each relation adds its
  own per-trace reads. All of it is bounded by the query caps every query carries.
