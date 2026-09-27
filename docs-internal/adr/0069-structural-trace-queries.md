# ADR-0069: Structural trace queries

Status: Accepted

Date: 2026-09-27

## Context

`SpanFilter` matches single spans. The Traces explorer could find "traces
with a `payment` span that errored", but not "traces where `checkout`
calls `payment` and that `payment` errored". Answering that needs
relationships between spans in the same trace. The roadmap asked for
operators for those relationships: `A -> B` (B is a direct child of A),
`A => B` (B is a descendant at any depth), plus AND/OR/NOT, returning the
matching traces. It left three questions open: how ClickHouse evaluates
them (a per-trace `groupArray` and a parent-id walk, or a self-join bounded
by `TraceId`), what that costs at scale, and the query syntax.
Prior art: [signoz#8165](https://github.com/SigNoz/signoz/commit/eeb2ab3212f20a7b6e8edda0a8a60c074469d0e6).

## Decision

- **A new `SpanFilter` field, not a new endpoint or page.**
  `SpanFilter.Structure` (`TraceStructureFilter`) holds up to 6 lettered
  span conditions (`TraceSpanCondition`: exact service, exact span name,
  status, minimum duration and `SpanAttributeFilter`s, all ANDed) and an
  expression over them. `SpanFilterSqlBuilder` compiles the structure to
  `TraceId GLOBAL IN (<subquery>)` over the filter's own time window. The
  Traces explorer's list, facet counts, value autocomplete, saved views and
  dashboard panels therefore all respect it, with no other changes.
- **Syntax.** A letter alone means "the trace has a matching span".
  `A -> B` means some B span's parent is an A span. `A => B` means some A
  span is an ancestor of some B span. A span is never its own parent or
  ancestor, even when it matches both conditions. `AND`/`&&`, `OR`/`||`,
  `NOT`/`!` and parentheses combine terms. The relation operators bind
  tightest, and their operands are single letters only. `A -> B -> C` is
  rejected: "B is A's child and C is that same B's child" can't be written
  as two independent pairwise tests, and reading it that way would give
  wrong answers without any warning.
- **An expression must need at least one matching span.** A trace with no
  span matching any condition must make the expression false, so `NOT A` on
  its own is rejected. Otherwise the query would return every trace in the
  window. It would also have to scan traces that no condition touches.
- **Evaluation: grouped arrays, no self-join.** Spans in the window that
  match any referenced condition are grouped by `TraceId`. For each
  condition X the query collects `hX` (has a match), `sX` (the matches'
  `cityHash64(SpanId)`) and `pX` (their hashed parent ids, 0 for roots).
  - `A -> B` is `hasAny(sA, pB)`, evaluated in one pass.
  - `A => B` needs the spans in between, which match neither condition, so
    it runs in two stages. Stage 1 is the pass above. There each `=>` is
    replaced by a bound that can only keep too many traces, never too few:
    `hA AND hB` where the relation counts for the trace, and the
    direct-child test where it's negated. Stage 2 re-reads every span of
    just those candidate traces (`TraceId IN`, cheap on the `TraceId`-first
    sort key). It collects the whole trace's span and parent hashes and
    evaluates exactly. The direct-child test runs first (a short-circuit),
    then an `arrayFold` that moves each B match up to its parent, one level
    per step, until it meets an A match. The fold runs at most
    `least(64, span count)` steps, so a trace with a malformed parent cycle
    still finishes.
  - `GLOBAL IN` is used for both the outer and inner subqueries, for the
    same reason as the entry-span filter (`SpanFilter.EntrySpansOnly`): in
    cluster mode the set is built once, not once per shard.
- **Invalid structures are rejected up front.** `POST /api/spans/search`
  returns 400 with the parser's or validator's message. A separate
  `POST /api/traces/structure/validate` runs only that check, without a
  query. The dashboard calls it before applying a structure, so an invalid
  one never reaches the list and facet requests that share the filter.
- **UI and CLI.** The Traces toolbar has a **Structure** button. It opens
  an editor with condition cards (the attribute rows are shared with the
  funnel step editor), an expression box and Apply/Remove. The applied
  expression shows on the button while the editor is closed. The CLI adds
  `flare traces --span "B:service=payment,status=error" --where "A => B"`.

### Spike

1M synthetic 6-span traces (6M spans, one 50-minute window, single node,
ClickHouse 26.8). `payment` was a direct child of `checkout` in 2/3 of the
traces and a grandchild (via `inventory`) in the rest, and it errored in
every 7th trace. Every result matched the generator exactly:

| Expression | Traces | Time | Peak memory |
|---|---|---|---|
| `A -> B`, structure subquery alone | 666,666 | 0.61 s | 179 MiB |
| `A => B`, structure subquery alone | 1,000,000 | 1.38 s | 667 MiB |
| `A => B AND NOT A -> B`, full explorer search through the API | 333,334 | 1.05 s | 308 MiB |
| `A => C` (C = payment with status error), in the dashboard | 142,858 | | |

The `A => B` worst case, where stage 1 keeps every trace and stage 2 reads
all 6M spans again, was tuned step by step:

| Variant | Time | Peak memory |
|---|---|---|
| Stage 2 with no walk (baseline for the two passes) | 1.12 s | 696 MiB |
| Walk over a fixed `range(32)` | 3.31 s | 770 MiB |
| Walk over a fixed `range(64)` | 4.60 s | 797 MiB |
| Walk over `range(least(64, length(allS)))` | 1.86 s | 691 MiB |
| Same, with the direct-child test first (chosen) | 1.38 s | 667 MiB |

`arrayFold` runs its lambda row by row, so bounding it by the trace's span
count and short-circuiting the common direct-child case matter most.

## Alternatives considered

- **Self-join bounded by `TraceId`.** One join level per tree level: `=>`
  would need a fixed maximum depth, one join per level, and a new query
  shape for each expression. The array approach handles any depth in one
  shape and reuses the funnel's per-trace grouping.
- **Evaluate `=>` in one pass over every span in the window.** Simpler SQL,
  but it holds full span arrays for every trace in the window, including
  traces that match no condition. The two-stage version holds them only for
  candidate traces.
- **A standalone "trace query" page with its own endpoint.** It would have
  duplicated the explorer's list, sorting, facets, saved views and
  dashboard panels. As a filter field, the explorer gets all of those for
  free.
- **Chains (`A -> B -> C`) meaning one shared B.** This needs a real
  pattern match over paths, not pairwise tests. Deferred until someone asks
  for it; it's rejected with a hint to use AND.

## Consequences

- Only spans that start inside the window take part. A trace that straddles
  the window's edge can miss a relation. So can a trace whose intermediate
  span was never ingested (sampled out, or from an uninstrumented hop),
  because the ancestor walk stops at the gap. The same caveat applies to
  funnels (ADR-0067).
- Neither stage can use migration 0025's `spans_by_start_time` projection.
  It lacks `Name` and `StatusCode`, and stage 2 needs whole traces. Cost
  grows with the spans matching the conditions, and, for `=>`, with the
  candidate traces' total span count. The usual `QuerySafety` caps bound
  both. The explorer runs the query when you click Apply, never on each
  keystroke.
- A structure narrows *which traces* are listed. Everything else in
  `SpanFilter` still applies to the listed rows (root spans, or entry spans
  with `EntrySpansOnly`). Facet counts count those rows, not the spans the
  conditions matched.
- The CLI has no per-condition attribute flags yet. The API and the
  dashboard do.
- Structures can't be used in alert rules yet. That would be new work.
