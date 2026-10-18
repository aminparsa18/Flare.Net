# ADR-0247: Prometheus API: `idelta`, `holt_winters`, `timestamp()`, `histogram_fraction` and `__name__` patterns

Status: Accepted

Date: 2026-10-18

## Context

[ADR-0246](0246-prometheus-api-time-scalar-delta-changes-resets-deriv-sort-count-values.md) left five things on the
roadmap. The same two facts decide how they are built: `metrics_gauge` keeps raw points, so a per-bucket aggregate of
them needs no migration, and a window is a whole number of step buckets.

## Decision

- **`idelta(x[5m])`** reads a new `MetricGaugeAggregate.LastDelta`: the newest point of each bucket minus the series'
  previous point (the window-function query ADR-0246 added for `delta`). The window takes its newest bucket's value.
  The previous point may lie before the window, so unlike Prometheus a window holding a single sample still has an
  `idelta`. Over a subquery it is the last two inner samples.
- **`timestamp(v)`**. For a plain selector it reads a new `MetricGaugeAggregate.LastTime`, the time in unix seconds of
  the newest point behind each bucket, so `time() - timestamp(x)` is the real age of a metric rather than the bucket
  start. For any other expression it is the step each sample sits on, as in Prometheus.
- **`holt_winters(x[5m], sf, tf)`**, with Prometheus 3's name `double_exponential_smoothing` as an alias, is
  Prometheus's smoothing recurrence over the window's bucket averages (so it needs two buckets, as it needs two
  samples). Factors outside `0 < f < 1` are a parse error. It also works over a subquery.
- **`histogram_fraction(lower, upper, v)`** is the share of observations between the bounds, with observations assumed
  evenly spread inside a bucket (the inverse of `histogram_quantile`'s interpolation). It takes the same argument
  shapes as `histogram_quantile` and shares its evaluation, which is now one helper parameterised by how a window of
  buckets is reduced to a number. The classic-histogram form is what an SLO ("fraction of requests under 300 ms")
  needs.
- **`__name__` patterns.** A selector may name its metrics with `=~` (`{__name__=~"http_.*"}`), optionally narrowed
  by further `__name__` matchers of any operator. The evaluator expands it, at the point of use, into one
  single-name selector per matching Prometheus name (alphabetical, at most 50, with a warning beyond that), evaluates
  each and concatenates the series, every series keeping its own `__name__`. Plain selectors, range functions
  (`rate({__name__=~"jobs_.*"}[5m])`), `timestamp()`, `/api/v1/series`, `/labels`, label values and `query_exemplars`
  all accept a pattern. A metric the surrounding expression cannot read (a counter outside `rate()`, a histogram
  under a gauge function) is skipped with a warning naming it and why; if every match fails, the first error is
  returned. `histogram_quantile` and `histogram_fraction` still need one name.
- **Not built, on purpose: `histogram_count`, `histogram_sum`, `histogram_avg`, `histogram_stddev`,
  `histogram_stdvar`.** They apply to native histograms. Flare serves classic `_bucket` series, and `_count`/`_sum`
  give the first three directly.

## Alternatives considered

- **A first/last-sample `idelta`** (two fetches). The newest point's step from its predecessor is one fetch.
- **Rewriting the AST once, before evaluation, to expand patterns.** Needs a visitor over every node type, and the
  metric list depends on the window each node reads (offsets, subqueries, `@`). Expanding where a selector is
  evaluated reads the right window for free.

## Consequences

- Grafana's metric browser (`{__name__=~"..."}` through `match[]`) and "everything with this prefix" panels work.
- A pattern pinned with `@` looks metrics up in the window of the query's own steps, not the pinned time, so a metric
  that only existed at the pinned time can be missed.
- The roadmap's PromQL item is finished. What stays outside is by design: `rate`/`increase` over a subquery
  (ADR-0245) and the native-histogram functions above.
