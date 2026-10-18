# ADR-0246: Prometheus API: `time()`, `scalar()`, `delta`, `changes`, `resets`, `deriv`, `predict_linear`, `sort*` and `count_values`

Status: Accepted

Date: 2026-10-18

## Context

[ADR-0245](0245-prometheus-api-at-modifier-subqueries-min-max-over-time.md) left a list of PromQL functions
rejected. Two of its findings decide how most of them can be built. `metrics_gauge` keeps one row per OTLP point, so
anything a query can compute from a series' raw points needs no migration and no ingest change. And the evaluator's
time model is bucket-based: a sample is stamped with its bucket's start, a `[5m]` window is `round(5m / step)` buckets,
and the newest bucket holds data only up to the query's end.

## Decision

- **`time()`** is a scalar that is each step's timestamp in seconds. An instant query (or a range query whose start
  equals its end) asks about one moment, so its last step reads that moment, not the bucket start.
- **`scalar(v)`** is `v`'s value at each step where it has exactly one series, NaN where it has none or several.
  Scalars are now a type of their own in the parser (`PromExprs.IsScalar`): a number, `time()`, `scalar(v)`, or
  arithmetic between them. Constant arithmetic still folds at parse time; anything involving `time()` or `scalar()` is
  evaluated per step, against a vector (`time() - process_start_time_seconds`) or another scalar. `vector()` takes any
  scalar, so `vector(time())` works. A scalar where a vector is required (`sum(time())`) is rejected, as in Prometheus.
- **`delta`, `changes` and `resets` over a gauge range** read three new `MetricGaugeAggregate` members (`Delta`,
  `Changes`, `Resets`; appended after `Count`, so existing ordinals and the hand-written `MetricQueryRequest.ts`
  wire format do not change). One window-function query compares every point with the series' previous point
  (`lagInFrame`, the same shape `BuildSumSql` uses) and attributes the comparison to the bucket holding the later
  point; the window then sums its buckets. That is exact for any number of points per bucket, one fetch each, and
  needs no migration.
  - Because a comparison belongs to the later point, a window includes the step from the sample just before it. So
    `delta(x[5m])` is the value at the window's end minus the value just before its start, and `changes`/`resets` count
    changes that happen inside the window. Prometheus counts only pairs of samples that are both inside the window, and
    `delta` extrapolates last-minus-first to the window edges; the two answers differ by at most the one step across the
    left edge. The first point a query reads has no predecessor and counts for nothing.
  - `delta`, `changes` and `resets` apply to gauges. A Prometheus counter scraped without type metadata arrives as a
    gauge, which is where `resets()` is used.
- **`deriv` and `predict_linear`** fit a least-squares line through the window's **bucket averages**, each placed at
  the middle of its bucket (the newest bucket, which holds data only to the query's end, at the middle of what it
  holds). Exact regression over raw points would need the points or five running sums per bucket, which the point
  model has no field for; for a trend over many buckets (the usual `predict_linear(free_bytes[6h], 4 * 3600)`) averaging
  inside a bucket changes the slope by a negligible amount. `predict_linear` evaluates the line at the end of the
  newest bucket (or the query end, if earlier) plus the seconds ahead. Fewer than two buckets give no sample, as in
  Prometheus.
- **Subqueries** accept the same new functions. `delta` is last minus first of the inner samples in the window (needs
  two, no extrapolation); `changes`, `resets`, `deriv` and `predict_linear` use the inner samples at their own
  timestamps.
- **`sort`, `sort_desc`, `sort_by_label`, `sort_by_label_desc`** reorder the series: by newest value with NaN last, or
  by label values in natural order (`node2` before `node10`). Only an instant query shows the order, as in Prometheus.
- **`count_values("label", v)`** with `by`/`without`: per group and timestamp, one series per distinct value holding
  how many series have it, with the value written into `label` as Prometheus formats a sample.
- A metric may share a function's name: `changes`, `time`, `sort`, `scalar` and the range functions are only functions
  when followed by `(`.
- `rate`, `increase` and `irate` over a subquery stay rejected for the reason in ADR-0245.

## Alternatives considered

- **Last minus first, as Prometheus does, for `delta`.** Needs the first and last value of every bucket, which is two
  more fetches for `delta` and three for `changes`/`resets` (they also need the boundary comparisons). The
  previous-point comparison is one fetch and is exact in its own terms.
- **Extrapolating `delta` to the window edges.** Needs the sample count and first/last timestamps per bucket; the
  previous-point reading already estimates "value now minus value a window ago" without them.
- **Exact `deriv` over raw points.** Would add per-bucket regression sums to `MetricSeriesPoint`, a field on a DTO the
  dashboard decodes, for a function whose input is already a smoothed trend.

## Consequences

- "Uptime" (`time() - process_start_time_seconds`), "restarts in the last day" (`resets(uptime_seconds[1d])`),
  "time until the disk fills" (`predict_linear`), "how many hosts run each version" (`count_values`) and sorted tables in
  Grafana's instant mode work unchanged.
- `changes` and `resets` count changes at the step's resolution only in the sense that a window is a whole number of
  steps; within a step every point is compared.
- Still rejected: `idelta`, `holt_winters`, `timestamp()`, the other `histogram_*` functions and `__name__` regex
  selection.
