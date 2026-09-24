# ADR-0047: `runningDiff` as a series-level Formula-mode function

Status: Accepted

Date: 2026-09-24

## Context

The roadmap asked for a point-to-point delta metric function: the change
between consecutive points of a series, e.g. for a gauge where the step
change matters more than the level. Two places could host it:

- **ClickHouse's `runningDifference`.** It resets at every data-block
  boundary, so a large enough series silently gets a wrong delta mid-chart.
  Working around that means a window function over the whole bucketed
  result, a new SQL shape in `MetricSeriesQueryBuilder`.
- **Metrics Explorer Formula mode (ADR-0036).** It already evaluates
  expressions app-side over fetched `MetricQueryResponse` rows. But its
  evaluator worked one bucket at a time, so no function could see a
  point's predecessor.

## Decision

Add `runningDiff(expr)` to the Formula-mode grammar
(`src/dashboard/src/lib/metrics/formula.ts`), next to `exp`/`log`/`sqrt`.
No backend changes.

- **Evaluation is now per joined series, not per bucket.** `evaluateVector`
  takes each referenced letter's values aligned to that series' common
  buckets and returns one value per bucket. `exp`/`log`/`sqrt`, the
  operators, and `null`/non-finite handling behave exactly as before; only
  the loop moved.
- **"Previous" means the previous defined point, not the previous time
  bucket.** A bucket dropped by the inner join, or undefined (e.g.
  `log(-1)`), is skipped, so a gap produces one delta spanning it. The first
  defined point has no predecessor and is dropped. This matches SigNoz's
  runningDiff (prior art:
  [signoz#5667](https://github.com/SigNoz/signoz/commit/4489df6f395fa6ba4e2cbc79011f10fb6c76e628)).
- **It composes like any other function.** `runningDiff(A / B)`,
  `runningDiff(A) * 60` and `runningDiff(runningDiff(A))` all work. Function
  names stay case-insensitive.

A single-metric user can use it by adding one query row `A` and the formula
`runningDiff(A)`, so Single mode's post-processing chain (ADR-0038) doesn't
get a copy.

## Consequences

- Formula mode can now express functions that depend on neighbouring
  points. Future series-level functions (e.g. moving windows) can plug into
  `evaluateVector` the same way.
- Dashboard Formula panels (ADR-0037) share `formula.ts`, so they get
  `runningDiff` too.
- A delta across a long gap reads as one large step. That follows from
  "previous defined point" and isn't flagged in the chart.
