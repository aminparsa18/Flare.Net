# ADR-0225: Comparisons, structural spansets and root-span fields in TraceQL metrics; what stays out

Status: Accepted

Date: 2026-10-16

Extends [ADR-0221](0221-traceql-metrics.md) and [ADR-0224](0224-traceql-metrics-topk-bottomk.md), which rejected all three.

## Decision

### A comparison after the function

- **`| rate() by (name) > 5`, with `=`, `!=`, `<`, `<=`, `>` and `>=`,** and a plain number. The grammar is Tempo's
  (`metricsSecondStagePipeline`, read from its `expr.y`): after the function, any chain of `| topk(n)`, `| bottomk(n)`
  and bare `op number` stages, applied left to right, then optional hints. `| rate() > 0 | topk(5)` drops the zero
  samples and then ranks what is left; `| topk(5) > 10` ranks and then drops the winners below 10. The two are not the
  same query, and the order written is the order run. At most 10 stages.
- **A sample that fails the comparison is dropped, as is an exemplar of that step, and a series left with no sample.**
  A step missing from a series stays missing (a line chart shows a gap). The comparison applies to every series the
  function computed, so for `quantile_over_time(duration, .5, .99) > 2` each quantile is filtered on its own, and for a
  histogram it filters bucket counts. In an instant query a series whose one value fails is left out.
- **The number is in the function's unit, seconds for a duration.** `> 500ms` is rejected, naming the rule: Tempo's
  grammar takes a scalar, and what a duration scalar compares against was not checked against a real Tempo, so it is
  not guessed.
- **In the API, after the ClickHouse query,** next to `topk` (ADR-0224), so the stages share one pipeline over the
  series (`TraceQlMetricsStage`: `TraceQlRank`, `TraceQlFilter`). `TempoMetricsPlan.Rank` became `Stages`.

### Structural spansets

- **`{ a } >> { b } | rate()` and `{ a } > { b } | rate()` count the right-hand spans that have a matching ancestor
  (`>>`) or a matching parent (`>`).** Tempo's structural operators return the right-hand spans. A trace that has
  both an `a` span and a `b` span but whose `b` spans have no `a` above them contributes nothing, which a trace-level
  `TraceId IN (structural subquery)` could not say. One relation, between two spansets; `&&` and `||` between spansets
  (union and intersection of span sets) are still rejected.
- **A direct parent is a semi-join:** `(TraceId, ParentSpanId) GLOBAL IN (SELECT TraceId, SpanId ... WHERE <a>)`.
- **An ancestor at any depth is a per-trace walk,** the same one the structure builder's `=>` uses (ADR-0069). The traces
  with both a left and a right span are collected; for each of them the spans' hashed ids and parents are grouped into
  arrays, and each right-hand span's parent chain is followed up to 64 steps (`arrayFold`), looking for a left span. The
  `(TraceId, SpanId)` pairs that pass restrict the outer aggregate. A span that matches both sides is not its own
  ancestor, because the walk starts at its parent. Prototyped against 200 spans of seeded traces (80 `db.query` spans
  under `svc.call`, 50 under each root name) and the counts matched a hand computation for `>` and `>>`.
- **The restriction is part of the filter,** so a `by` clause's top-group ranking, the exemplar query and the
  aggregate agree about which spans count.
- **Cost:** one extra read of the matching spans of the candidate traces, on top of the aggregate (two with `by`).
  On the 47,000-span test table a 30-day query ran in under 100 ms; the cost model is the structure builder's, not
  the single scan a plain spanset is.
- **Look-back.** The ancestor, and the root below, come before the span, so they are looked for from one hour before
  the window (`TraceQlMetricsQueryBuilder.Lookback`, the service map's `ParentStartSlack`, ADR-0043). An ancestor that
  started earlier, or was never ingested, means its descendants are not counted.

### Root-span fields

- **`{ rootName = "GET /checkout" }`, `rootServiceName` and `traceDuration`** restrict the spans counted to those of
  traces whose root row matches, the way a search restricts the traces it lists to the roots that match (the root being
  the span with no parent, and `traceDuration` its duration, as in a search). `{ rootName = "x" } | rate()` is every
  span of such traces, `{ rootName = "x" && name = "db.query" }` only the database ones. Compiled by
  `TempoSearchPlanner` as before; the planner already rejects them next to several spansets, so they do not combine
  with a relation.
- **`TraceId GLOBAL IN (SELECT TraceId FROM spans WHERE ParentSpanId = '' AND ...)`** over the look-back window,
  with the service scope applied inside the subquery.

### Exemplars on an instant query: no

Checked against Tempo's `tempo.proto`: `QueryInstantRequest` has `query`, `start` and `end` and no `exemplars`;
`InstantSeries` has `labels` and `value` and no exemplars. Tempo returns none for an instant query, and Grafana (which
asks with `exemplars=N` only on a range request) has nothing to draw them on, since an instant value has no time axis.
There is no consumer, so none is built; the roadmap line is dropped.

## Not done

- **`compare({ selection })`.** ADR-0224 gave the reasons it is a query type of its own, and it still is: Tempo's engine
  emits, per attribute, the top-N values of the selection and of the baseline (`__meta_type` of `selection`, `baseline`,
  `selection_total`, `baseline_total`, and `__meta_error` for too many values), which is a three-map `ARRAY JOIN` scan
  of every span. The decisive finding is who calls it: Grafana's Traces Drilldown, its Comparison tab. That app was
  installed into Grafana 13.2.4 and pointed at Flare, and its first requests are rejected before `compare()` is ever
  reached. It sends `{nestedSetParent<0 && true && status=error} | rate() with(sample=true)`,
  `{... && resource.service.name != nil} | rate() by(resource.service.name)` and
  `histogram_over_time(duration) with(sample=true)`: the `nestedSetParent` intrinsic (root spans), a bare `true`, the
  `!= nil` test, the `sample` hint, and `by(` without a space. Without those the app shows a query error on its first
  screen, so `compare()` alone would be a function nothing can use. Both are on the roadmap, in that order.
- **`&&` and `||` between spansets in a metrics query,** and the sibling and negated structural operators.
- **A duration as the comparison's number** (`> 500ms`), see above.

## Consequences

- `{ kind = server } | rate() by (span.http.route) > 5` and `{ ... } | rate() by (name) > 0 | topk(5)` work in a
  Grafana panel, as do `{ name = "svc.call" } >> { name = "db.query" } | quantile_over_time(duration, .95)` and
  `{ rootName = "GET /checkout" } | count_over_time()`.
- A structural or root-restricted metrics query costs more than a plain one (a read of the candidate traces); it is
  bounded by the query caps every query carries, and a query over a wide window with loose conditions is slow.
- The error for `rootName` next to a relation, and for an empty side of a relation, comes from the search planner and
  names the rule.
