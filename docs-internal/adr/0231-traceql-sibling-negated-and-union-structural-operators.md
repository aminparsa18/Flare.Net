# ADR-0231: TraceQL sibling, negated and union structural operators

Status: Accepted

Date: 2026-10-16

Extends [ADR-0069](0069-structural-trace-queries.md), [ADR-0225](0225-traceql-metrics-comparison-and-span-relations.md) and
[ADR-0227](0227-traceql-select-union-descendant-and-nested-sets.md), which built `>`, `>>` and `&>>` and left the rest. Third part of the
fixed list started in [ADR-0229](0229-traceql-kind-sets-and-colon-intrinsics.md).

## What Tempo does (read from its source, `pkg/traceql/ast_execute.go` and `tempodb/encoding/vparquet4/block_traceql.go`)

Every structural operator is evaluated on one trace's spans, with the left-hand set `L` and the right-hand set `R`, and **returns spans of `R`**
(of both for a union):

| Operator | Result |
|---|---|
| `>`, `>>`, `~` | the `R` spans with a child-of / descendant-of / sibling-of relation to some span of `L` |
| `!>`, `!>>`, `!~` | the `R` spans with **no** such span in `L` |
| `&>`, `&>>`, `&~` | the spans of both sides that take part: `R` spans with a related `L` span and the `L` spans with a related `R` span |

Three details settle the SQL: a span is never its own sibling; **two root spans of a trace are siblings** (a root's parent position is -1 for both);
and the negated forms do not need a left-hand span to exist, only a right-hand one (`joinSpansets` passes an empty `L` on), so `A !> B` lists every
`B` span of a trace that has no `A` at all. A trace is returned when the result is not empty.

## Decision

### The expression grammar, and what a relation is

`TraceStructureRelation(Parent, Child, Kind, Negated)` replaces the pair of `->` / `=>`: `Kind` is `Child`, `Descendant` or `Sibling`. The structure
expression grammar (the one behind the Traces page's structure builder too) gains `~`, `!->`, `!=>` and `!~`; `Parent` is the left-hand condition
and `Child` the right-hand one, whatever the kind. A union operator is the plain relation for the trace-level question and is remembered only to
decide which spans are listed. The original `->` and `=>` read, parse and compile as before (the existing tests are unchanged).

### Which traces match (search; `TraceStructureSqlBuilder`)

One extra array per relation, aggregated per trace in the same pass as before:

- **`A !-> B`** (not a child): `arrayExists(p -> NOT has(sA, p), pB)`, some `B` span whose parent hash is not an `A` span's. A root has parent hash 0.
- **`A ~ B`** (sibling): `pA` holds the parent hashes of `A` spans and `bAB` the pairs `(parent hash, is it also an A span)` of `B` spans.
  `arrayExists(b -> if(b.2 = 1, countEqual(pA, b.1) >= 2, has(pA, b.1)), bAB)`: a `B` span that is not itself an `A` span needs one `A` under its
  parent, one that is needs two. **`A !~ B`** is the same with `NOT` inside the lambda.
- **`A !=> B`** (not a descendant) is the second-stage walk of `=>` with the accumulator carrying, per `B` span, whether an `A` ancestor has been met:
  `arrayFold` over `(frontier, flags)`, then `arrayExists(f -> f = 0, flags)`. Stage 1, which has no whole-trace arrays yet, keeps a trace whenever
  it could match: `hB` in a positive position, and false under a `NOT` (a lower bound of "some span has no ancestor in A" is nothing), so the
  expression `A AND NOT (A !=> B)` still reaches stage 2 with every candidate.
- `->`, `~` and their negations are exact in one pass; only `=>` and `!=>` need the second stage.

### Which spans a search lists

The relations whose result differs from "every span matching any condition" - the negated ones, `~`, and the unions - go through the selector
(ADR-0227), which now takes any relation, not only `&>>`. For each `R` span in a trace, `TempoSpanSelector` finds the related `L` spans (the parent,
the ancestors up to 64 levels, or the spans with the same parent id); the span is listed when there is one (none, for a negated operator), and for
a union the `L` spans found are listed too. Siblings are compared by parent id text, so two roots (empty id) are siblings. A plain `>` and `>>`
keep listing every span that matches a condition (ADR-0219), as before.

### Metrics

`{ a } ~ { b } | rate()` and the negated forms count the `b` spans on the same footing as `>>` did (ADR-0225), the clause depending on the kind:

| Operator | Clause on the counted span |
|---|---|
| `!>` | `(TraceId, ParentSpanId) GLOBAL NOT IN (the A spans' ids)` |
| `~` | another `A` span under the same parent: `(TraceId, ParentSpanId) IN (parents with two or more A spans)` OR (the counted span is not itself an `A` span AND its parent is in the set of parents with an `A` span) |
| `!~` | `NOT` the above |
| `!>>` | `(TraceId, SpanId) GLOBAL NOT IN (the descendant walk's set of counted spans that have an A ancestor)`: a trace with no `A` span has an empty set, so all its `b` spans count |

Look-back is the same hour as ADR-0225. Union operators (`&>>`, `&>`, `&~`) are rejected in a metrics query naming the operator (they return both
sides; a metrics query has one). `by`, exemplars and the ranking of the top groups all see the same clause.

### Not in this change

`<`, `<<`, `!<`, `!<<`, `&<`, `&<<` (the right-hand span as the parent or ancestor): not in the fixed list. They are parse errors (`unexpected '<'`).

## Verified

Against ClickHouse on the test table plus 345 seeded spans (60 traces: a root, `call-a` > `handle-a` > `db.query`, `call-b` > `handle-b` > `cache.get`,
and a `log` span, as siblings under the root), each result compared with a hand-written `JOIN` (independent of the builders). `count_over_time()`:

| Query | API | SQL |
|---|---|---|
| `{call-a} ~ {call-b}` | 38 | 38 |
| `{client, front} ~ {client, front}` (the span matches both sides) | 76 | 76 |
| `{call-a} !~ {log}` | 6 | 6 |
| `{call-a} !> {handle-b}` / `{call-b} !> {handle-b}` | 41 / none | 41 / 0 |
| `{call-b} !>> {db.query}` / `{call-a} !>> {db.query}` | 40 / none | 40 / 0 |
| `{handle-a} !>> {back service}` | 120 | 120 |
| `{no such span} !>> {cache.get}` (no left-hand span at all) | 29 | 29 |

The search returned the same traces and, for every one of 11 operator queries, the same listed spans as SQL (right-hand spans; both sides for `&~`
(76 spans of 38 traces), `&>` (100 of 50) and `&>>` (80 of 40); 120 spans of 53 traces for `!>>`; 41 for `!>`; 6 for `!~`). `by (name)` and
`with(exemplars=true)` run on the new relations.

## Consequences

- Tempo queries with `~`, `!>`, `!>>`, `!~`, `&>` and `&~` run in search, and in a metrics query (except the unions).
- A negated operator is a per-span test in a trace: it is as cheap as `>>` in a search and, in a metrics query, one extra `NOT IN`.
- A sibling test needs the parent id: a span whose parent was never ingested is a sibling of every other span with that same missing parent.
