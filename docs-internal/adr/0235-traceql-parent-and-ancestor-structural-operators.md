# ADR-0235: TraceQL parent and ancestor structural operators

Status: Accepted

Date: 2026-10-16

Extends [ADR-0231](0231-traceql-sibling-negated-and-union-structural-operators.md) and [ADR-0232](0232-traceql-metrics-spanset-combinations.md), which
built `>`, `>>`, `~`, their negations and unions and left `<`, `<<` and their forms out. Last part of the second fixed list started in
[ADR-0233](0233-traceql-event-name-status-message-filters-and-typed-group-labels.md); with it every operator of Tempo's spanset grammar that has a Flare
meaning is accepted.

## What Tempo does (read from its source, `pkg/traceql/ast_execute.go` and `tempodb/encoding/vparquet4/block_traceql.go`)

`SpansetOperation.evaluate` passes the left-hand spans `L` and the right-hand spans `R` of a trace to `ChildOf` (for `<`, `!<`, `&<`) or `DescendantOf`
(`<<`, `!<<`, `&<<`) with `invert = true`. With `invert` the roles inside the functions are exchanged, but the result is still taken from `R`:

| Operator | Result |
|---|---|
| `{L} < {R}` | the `R` spans that are the **parent** of some `L` span |
| `{L} << {R}` | the `R` spans that are an **ancestor** of some `L` span |
| `{L} !< {R}`, `{L} !<< {R}` | the `R` spans that are the parent / an ancestor of **no** `L` span (`falseForAll`); with no `L` span in the trace, every `R` span |
| `{L} &< {R}`, `{L} &<< {R}` | `R` spans above an `L` span **and** the `L` spans below an `R` span (the union loops swap parents and children and add both ends of each match) |

Same as the other operators: a span is never its own parent or ancestor, and a trace is returned when the result is not empty. Note that `>` lists the span
below and `<` the span above: `{A} < {B}` and `{B} > {A}` ask the same question of a trace but list different spans. A union is the same set read from either
side: `{A} &< {B}` is `{B} &> {A}`.

## Decision

### The expression grammar

`TraceRelationKind` gains `Parent` and `Ancestor`, "the right-hand span is the parent (an ancestor) of the left-hand one". The structure expression
(ADR-0069, the one behind the Traces page too) gains `<-` and `<=` and the negations `!<-`, `!<=`: `A <- B` reads "A's parent is B". As before `Parent` is the
left-hand letter and `Child` the right-hand one, whatever the kind; `TraceStructureRelation.Upper` and `.Lower` say which of the two is higher in the
trace, so each consumer is written once for "the upper side's span hashes" and "the lower side's parent hashes". A union is the plain relation for the
trace-level question and is remembered by the TraceQL parser (`TraceQlRelation.Union`) only to decide which spans are listed. The parser records
every `<` and `<<` as such a relation, plain ones included: unlike `>` and `>>`, they list the span above, not every span matching a condition.

TraceQL's tokenizer reads `<<`, `&<`, `&<<`, `!<`, `!<<` besides the `<` it already had for comparisons; inside braces nothing changes, since `<` there is
read as the comparison it always was.

### Which traces match (search; `TraceStructureSqlBuilder`)

The aliases per trace are the same as for `->` and `=>`, chosen by role: `s` (span hashes) for the upper side, `p` (parent hashes) for the lower.

| Operator | Condition on the trace |
|---|---|
| `A <- B` | `hasAny(sB, pA)` |
| `A !<- B` | `arrayExists(u -> NOT has(pA, u), sB)`: a `B` span that is no `A` span's parent (no `A` span: every `B` span qualifies) |
| `A <= B` | the `=>` walk with the sides exchanged: the `A` spans' ancestors, a level per step, looking for a `B` hash |
| `A !<= B` | the same fold, accumulating every ancestor (`arrayConcat`), then `arrayExists(u -> NOT has(<ancestors>, u), sB)` |

`<-` and `!<-` are exact in one pass. The two ancestor forms need the spans in between, so they are second-stage like `=>` and `!=>`: stage 1 keeps a trace
when `hA AND hB` (positive) or `hB` (`!<=`, positive); under a `NOT` the bound is the direct test, or false for `!<=`, as for `!=>` (ADR-0231).

### Which spans a search lists

`TempoSpanSelector` already takes any relation. For a right-hand span it now also finds the left-hand spans below it: its children (`<`) or its descendants,
breadth first up to 64 levels, the same limit as the ancestors walk (`<<`). The span is listed when there is one (none, for `!<` and `!<<`), and for a union the
left-hand spans found are listed too.

### Metrics

The counted spans are the right-hand ones, as for every relation (ADR-0225):

| Operator | Clause on the counted span |
|---|---|
| `<` | `(TraceId, SpanId) GLOBAL IN (the parent ids of the matching A spans)` |
| `<<` | `(TraceId, cityHash64(SpanId)) GLOBAL IN (every ancestor, up to 64 levels, of the matching A spans)` |
| `!<`, `!<<` | the same sets with `NOT IN`; a trace with no `A` span has an empty set, so all its counted spans qualify |
| `&<`, `&<<` | exchanged into `{B} &> {A}` / `{B} &>> {A}` (the same set of spans) |

These are the sets `InverseRelationClause` built for the other half of a union (ADR-0232), which gains a `negated` flag; `RelationClause` uses it for the new kinds.
They work as `{ a } < { b } | rate()` (a `SpanAncestor`), inside `&&` / `||` combinations (`SpanSetRelation`) and as the selection of `compare()` (ADR-0234). `by`,
exemplars and the ranking of top groups all see the same clause.

## Verified

Against ClickHouse with 444 seeded spans (60 traces: `root` > `call-a` > `handle-a` > `db.query`; `root` > `call-b` > `handle-b` > `cache.get` in 48 of them; a `log`
span under the root; three scopes, two services). For eight pairs of conditions (by name, by kind, a left side that matches nothing, one where the same span can
match both sides) and all six operators, 48 queries, **the metrics count, the listed spans and the set of traces returned by a search each equalled a hand-written
SQL**. The SQL builds the parent/ancestor pairs with `INNER JOIN`s (explicit chains of up to four levels for ancestors, not a hashed array), and the negated and union
forms with `LEFT JOIN` and `UNION DISTINCT` over those pairs, so nothing of the builders' approach is shared. A selection:

| Query | API | SQL |
|---|---|---|
| `{db.query} < {handle-a}` / `<<` | 60 / 60 | 60 / 60 |
| `{db.query} < {handle-a, call-a, root}` / `<<` / `!<` / `!<<` | 60 / 180 / 120 / 0 | 60 / 180 / 120 / 0 |
| `{db.query} &< {...}` / `&<<` (both sides) | 120 / 240 | 120 / 240 |
| `{db.query} !< {six names}` / `!<<` | 276 / 156 | 276 / 156 |
| `{cache.get} !<< {call-b, root, handle-b}` (only the 12 traces without `call-b`) | 12 | 12 |
| `{no such span} !< {call-a, root}` / `!<<` (no left-hand span) | 120 / 120 | 120 / 120 |
| `{kind = server} << {kind = server}` (a span on both sides) | 60 | 60 |
| `{kind = server} &<< {kind = server}` | 168 | 168 |

Also run and equal to SQL: `({db.query} << {root, call-a}) || {cache.get}` (168), `... && {log}` (180, the union of both sides in traces that have each), `by (name)` over
`<<` (60 each for `root`, `call-a`, `handle-a`), a range query with `with(exemplars=true)`, and `compare({db.query} << {root, call-a})` (selection `root` and `call-a`
60 each; `&<<` adds the 60 `db.query` spans).

## Consequences

- Tempo queries that use `<`, `<<` and their negated and union forms run in search and in metrics; nothing is left of the spanset grammar that Flare has no meaning for.
- `<-` and `<=` are new in the Traces page's expression language too. The dashboard's structure builder does not offer them; an API client can send them.
- An ancestor test costs what a descendant test costs: a second read of the candidate traces, bounded by the query caps and the 64-level walk.
