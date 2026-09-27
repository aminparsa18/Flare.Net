# ADR-0067: Trace funnels

Status: Accepted

Date: 2026-09-27

## Context

The roadmap asked for trace funnels: define an ordered list of steps (for
example checkout → payment → confirmation), then measure across the traces
in a window how many reach each step, where they drop off, and the
step-to-step latency and error rate, with a drill-down into the traces behind
each figure. It flagged three open questions: how a step matches, what
"in order" means inside one trace, and what the query costs at scale.
SigNoz's first attempt was reverted, so the roadmap asked for a spike first.
Prior art: [signoz#7315](https://github.com/SigNoz/signoz/commit/3100d602c43f12b2b7b5f029d2c8ea29531adda7),
list page [signoz#7324](https://github.com/SigNoz/signoz/commit/2c87d96d753e9a786234e707783413f9de25e672).

## Decision

- **A step is a span match.** `TraceFunnelStep` has an optional exact
  `ServiceName`, an optional exact `SpanName`, and optional
  `SpanAttributeFilter`s with the same operators as the Traces explorer. All
  set conditions must hold, and at least one must be set. Funnels have 2 to
  6 steps.
- **Greedy, in-order matching inside a trace.** A trace enters at its
  earliest span matching step 1. Each later step takes the earliest matching
  span that starts at or after the previous step's span and isn't that same
  span. A trace's level is how far that walk gets. A step matched only
  *before* the previous one doesn't count, but a later match of it does.
  The step's error status and the transition latency come from the span the
  walk picked, so a failed attempt followed by a successful retry counts as
  reached, with an error. The
  latency is start of the previous step's span to start of this step's
  span.
- **Computed at query time from `spans`, in one query.** The inner query
  reads the window's spans that match any step. When every step names a
  service, it adds a `ServiceName IN` prefilter so `idx_service` can skip
  granules. It groups by `TraceId` into one sorted
  `(StartTime, cityHash64(SpanId), isError)` array per step. The middle query
  walks the arrays with `arrayFirstIndex`, and the outer query aggregates
  (`countIf`, `avgIf`, `quantilesIf`) or lists traces for the drill-down.
  Built by `TraceFunnelQueryBuilder`, served by
  `POST /api/traces/funnel` and `POST /api/traces/funnel/traces`.
- **The window is 5 minutes to 24 hours**, the same range as the Services
  and Messaging views. Only spans that start inside it count.
- **Saved funnels are saved views.** `SavedViewPageType.Funnels` is appended
  to the enum. `saved_views.PageType` is already a string column, so no
  migration is needed, and the Views menu, `?view=` links, last-used restore
  and the `/views` page work as they do for the explorers. The page lives at
  `/traces/funnels`, as a third tab next to Traces and Services.

### Spike

2M synthetic checkout traces (12.6M spans, one hour, single node, ClickHouse
26.8) went through a three-step funnel with production `QuerySafety` caps.
The result was exact against the generator: 2,000,000 entered, 1,400,419
reached payment, 69,659 payment errors, 1,197,610 reached confirmation. It
took ~1.2 s and ~490 MiB, and the drill-down query cost about the same.
`optimize_aggregation_in_order` saved ~20% memory but ran ~25% slower, so
it's left off. The 10,000-row `max_result_rows` cap (`result_overflow_mode =
'break'`) didn't truncate the 2M-row per-trace subquery. Hand-built traces
confirmed the ordering rules: a step matched only before the previous one
doesn't count, a later retry does, and one span can't satisfy two
consecutive steps.

## Alternatives considered

- **ClickHouse `windowFunnel`.** It returns only the level reached. It gives
  no per-step timestamps for latency and no per-step status for errors, so
  we'd still need the arrays.
- **Earliest match per step, then check the times are non-decreasing.**
  Simpler SQL, but a retry of an earlier step makes the trace look like it
  dropped off. The greedy walk handles retries at little extra cost.
- **A pre-aggregated table.** Steps are arbitrary and user-defined, so
  there's nothing fixed to pre-aggregate.

## Consequences

- The query can't use migration 0025's `spans_by_start_time` projection,
  which lacks `Name` and `StatusCode`. On the `TraceId`-first base table a
  time window prunes only by monthly partition. Cost grows with the spans
  matching the steps and memory with the traces entering the funnel. The
  usual scan/time caps bound it. The page runs only when you click Run, or
  when you change the window after a run. It never re-runs on each
  keystroke.
- A trace that straddles the window's end can look like it dropped off.
- In cluster mode `spans` is sharded by `cityHash64(TraceId)`, so every
  trace's spans sit on one shard. The normal distributed two-stage
  aggregation stays correct.
- Drill-downs return at most 100 traces, most recent first.
- Only spans are steps. Log or event steps, and funnel alerts, would be new
  work.
