# ADR-0239: Prometheus API: binary operators, offset, topk/bottomk/quantile and more functions

Status: Accepted

Date: 2026-10-17

## Context

ADR-0109 shipped a deliberately small PromQL subset and named what it left out: operators between series, `offset`,
`topk`, `quantile` and other functions. Those are the shapes Grafana panels are mostly made of. An error ratio is
`sum(rate(errors[5m])) / sum(rate(requests[5m]))`, "top 5 hosts by CPU" is `topk`, "versus last week" is
`offset 1w`, and a percentage axis is `100 * x`. ADR-0109 rejected the ratio case because label matching is where a
subset starts to mislead. That risk is real, so the matching rules here are Prometheus's, not an approximation.

## Decision

- **Binary operators follow Prometheus's precedence and matching.** Arithmetic `+ - * / % ^`, comparisons
  `== != > < >= <=` (filtering by default, `bool` to return 1/0) and the set operators `and`, `or`, `unless`, with
  Prometheus's precedence (`or` < `and`/`unless` < comparisons < `+ -` < `* / %` < `^`, `^` right-associative).
  A vector is matched to a vector on its labels minus `__name__`; `on(...)`/`ignoring(...)` narrow that, and
  `group_left`/`group_right` (with an optional label list copied from the "one" side) allow many-to-one. A scalar on
  either side applies to every sample. Two scalars are still folded at parse time (`1+1`), and a comparison between
  scalars without `bool` is an error as in Prometheus.
- **Matching is checked per timestamp, and a clash is an error, not a pick.** Two series on the "one" side with a
  sample at the same instant fail with Prometheus's "found duplicate series for the match group" and point at
  `group_left`/`group_right` or `on()`. Series that share a signature but never overlap in time do not clash. The
  result keeps the many side's labels (reduced to the `on` labels, or minus the `ignoring` ones, for one-to-one) and
  drops `__name__` for arithmetic and `bool` comparisons.
  Division by zero is `+Inf`/`NaN`, written as the strings Prometheus uses.
- **`offset` on a selector or range selector** (`x offset 1h`, `rate(x[5m] offset 1d)`). The window is fetched that
  much earlier and the samples are re-stamped to the evaluation time, so an offset series lines up with the current
  one in a binary operator. The offset is rounded to a whole number of steps, because buckets are aligned to the
  step (ADR-0109) and a fractional shift would put the series off the others' timestamps. Negative offsets and `@`
  stay rejected.
- **More aggregations.** `topk`/`bottomk` (per timestamp, within each `by`/`without` group; the series keep their own
  labels and appear only at the instants where they rank), `quantile` (Prometheus's interpolation between the two
  nearest ranks), `group`, `stddev` and `stdvar` (population). The parameter must be a number literal.
- **More functions.** `irate`, `avg_over_time` and `last_over_time` over a range selector; `abs ceil floor round sqrt
  exp ln log2 log10 sgn clamp clamp_min clamp_max` on an instant vector; `vector(s)`; unary minus on a vector.
- **Honest about the bucket model.** `irate` is the newest bucket's increase per second, not the last two raw
  samples. `avg_over_time` averages bucket averages, and `last_over_time` is the newest bucket's average, because
  Flare stores a gauge as one average per bucket. `min_over_time`, `max_over_time`, `sum_over_time` and
  `count_over_time` are therefore **not** offered: over averaged buckets they would return a number that looks exact
  and is not. They are rejected by name, like every unsupported function.
- **Where it lives.** The pure operator semantics are in `PromBinaryOperations` and `PromFunctions`, next to the
  parser's new AST nodes (`PromBinary`, `PromFunction`), so they are unit-tested without a metric service. The
  evaluator evaluates a scalar operand as a constant rather than a series, so `100 * x` is one metric query.

## Alternatives considered

- **Push binary operators into ClickHouse.** Rejected for the reason ADR-0109 gave: it would reimplement the
  reset-aware `increase()` and histogram temporality per operator. Both sides are fetched through the same
  `IMetricQueryService` and joined in memory, so a ratio of two rates cannot disagree with either dashboard panel.
- **Offer `min_over_time`/`max_over_time` on bucket averages.** Rejected: see above. A max of averages
  understates a spike, which is the one thing a max is read for.
- **Match on `__name__` too.** Rejected: Prometheus ignores it, and `errors / requests` would never match.

## Consequences

- An error ratio, a top-N panel, a week-over-week comparison and a percentage axis work from Grafana unchanged.
- Each side of a binary operator is its own fetch capped at 200 series (ADR-0109), so a ratio of two wide selectors
  can lose series from either side; the warnings say which. Aggregate first (`sum by (job) (...)`) to stay under it.
- Exemplars are not part of this change. They need a place to store them at ingest (the metric tables hold none), so
  they stay on the roadmap.
- Still rejected: `@`, subqueries, `label_replace`/`label_join`, `absent`, `time()`, `scalar()`, `predict_linear`,
  `deriv`, `delta`, `changes`, `resets`, `sort*`, `count_values`, and `__name__` regex selection.
