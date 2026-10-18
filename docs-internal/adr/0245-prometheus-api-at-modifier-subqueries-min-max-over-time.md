# ADR-0245: Prometheus API: the `@` modifier, subqueries and `min`/`max`/`sum`/`count_over_time`

Status: Accepted

Date: 2026-10-18

## Context

[ADR-0239](0239-prometheus-api-binary-operators-offset-topk-functions.md) rejected `min_over_time` and
`max_over_time` because "Flare stores a gauge as one average per bucket", so a max would be a max of averages and
understate the spike a max is read for. It listed "needs per-bucket min and max stored" as the fix. That premise was
too pessimistic: `metrics_gauge` stores one row per OTLP data point (migration 0008), and only the query reduces them
with `avg(Value)`. A bucket's minimum, maximum, sum and count are one `GROUP BY` away, with no migration and no
ingest change. `@` and subqueries were rejected only because nothing implemented them.

## Decision

- **`min_over_time`, `max_over_time`, `sum_over_time`, `count_over_time` over a gauge range selector** read a new
  `MetricQueryRequest.GaugeAggregate` (Min, Max, Sum, Count; null = average, appended last per the MemoryPack
  convention). The gauge query then reduces each step bucket's raw points with that aggregate, and the window folds
  the buckets (min of mins, max of maxes, sum of sums, sum of counts). That is exact for the step grid, so the
  ADR-0239 objection no longer applies. The top-N series ranking still uses the average. `avg_over_time` and
  `last_over_time` are unchanged. The dashboard never sets the field.
- **`@`** on a selector, range selector or subquery: `x @ 1700000000`, `x @ start()`, `x @ end()`, combined with
  `offset` in either order. The expression is evaluated once, as an instant query at the pinned time (the newest
  sample at or before it, within the five-minute lookback instant queries use), and that sample is repeated at every
  step. `offset` counts back from the pinned time, as in Prometheus. In an instant query
  `start()` and `end()` both mean the evaluation time, not the edges of the five-minute lookback window the endpoint
  evaluates over. Negative and fractional timestamps parse.
- **Subqueries `expr[range:resolution]`** as the argument of `avg|min|max|sum|count|last_over_time`, with optional
  `offset` and `@` after the bracket. The inner expression is evaluated on its own grid (`resolution`, the query step
  when omitted) over the window of every outer step, then each outer step reduces the inner samples in
  `(t - range, t]`. Inner samples are bucket starts, so a window can include the bucket that starts at its end; that
  is the same convention range functions already use. The inner grid is capped at the 11,000 points Prometheus allows.
- **`rate`, `increase` and `irate` over a subquery stay rejected.** For a selector, Flare's counter buckets hold
  per-interval increases and those functions sum them. A subquery's values are rates or levels, not increases, so
  reusing the formula would give a wrong number. The error says to apply `rate()` inside the subquery and use an
  `*_over_time` function outside it, which is the shape Grafana panels use (`max_over_time(rate(x[5m])[1h:1m])`).
- Parsing adds `PromAt`, `PromSubquery` and `PromSubqueryFunction`; the evaluator pins with one helper
  (`PinnedAsync`) shared by selectors, range functions, subquery functions and `histogram_quantile`.

## Alternatives considered

- **Store min and max per bucket at ingest.** Rejected: the raw rows already hold them, and a stored per-bucket
  aggregate would be tied to one step width.
- **Make `avg_over_time` exact too (sum/count).** Deferred: it needs two fetches per query, and an average of bucket
  averages is a reasonable reading of an average, unlike a max.
- **Support `rate` over a subquery with Prometheus's first/last extrapolation.** Rejected for now; see above.

## Consequences

- "Peak error rate over the last hour" (`max_over_time(rate(errors_total[5m])[1h:1m])`), "the lowest free memory
  today" (`min_over_time(free_bytes[1d])`) and "compared with the start of the dashboard range" (`x / x @ start()`)
  work from Grafana unchanged.
- `max_over_time` over a gauge is exact only down to the step: points within one step bucket are reduced exactly, but
  a window is a whole number of steps.
- Still rejected: `time()`, `scalar()`, `predict_linear`, `deriv`, `delta`, `changes`, `resets`, `sort*`,
  `count_values` and `__name__` regex selection.
