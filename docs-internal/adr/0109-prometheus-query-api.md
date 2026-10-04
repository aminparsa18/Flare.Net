# ADR-0109: A Prometheus-compatible query API subset

Status: accepted

## Context

Grafana and `prometheus-adapter` (Kubernetes HPA on custom metrics) speak the Prometheus HTTP API.
Flare stores OTel metrics in ClickHouse and has no such endpoint, so neither tool can read it. Full
PromQL is a large language with its own evaluation model (staleness, lookback, label matching across
binary operators). Reimplementing it would be a project of its own, and an incomplete PromQL that
silently returns wrong numbers is worse than none.

## Decision

- **A read-only subset of the HTTP API under `/api/v1`,** on the normal authenticated route group:
  `query`, `query_range`, `series`, `labels`, `label/<name>/values` and `status/buildinfo` (Grafana's
  connection test reads it). GET and form-encoded POST both work. Auth is the existing pipeline, so a
  personal access token (ADR-0019) as `Authorization: Bearer flr_pat_...` is all a client needs, and
  PAT rate limiting (ADR-0028) applies.
- **A PromQL subset with a hand-written parser,** `PromQlParser`: vector selectors with `= != =~ !~`,
  `rate`/`increase` over a range selector, `sum|avg|min|max|count` with `by`/`without`,
  `histogram_quantile`, and arithmetic between number literals (Grafana's test query is `1+1`).
  Everything else (vector/vector operators, `offset`, `@`, subqueries, other functions) throws a
  `bad_data` error that names the construct. The parser is the single place that decides what is in
  scope; the evaluator never sees an unsupported node.
- **A translator over `IMetricQueryService`, not new SQL.** Each selector becomes a `MetricQueryRequest`
  with the query step as the bucket width, so the Prometheus path inherits the reset-aware `increase()`
  (ADR-0035), temporality-aware histograms (ADR-0060), the result cache (ADR-0029), query caps and
  `treat gauge as counter` (ADR-0066). A dashboard's Rate mode and a Prometheus `rate()` cannot disagree.
  The cost is that grouping and aggregation happen in memory: the series query groups by at most one
  attribute key, and `sum by (a, b)` needs any label set. Series are therefore fetched at full label
  granularity (capped at 200 by the metric query service) and folded in the evaluator. Hitting the cap
  adds a Prometheus `warnings` entry; the answer is never silently truncated.
- **Naming follows the OTel Prometheus compatibility rules** (`PromNames`): characters outside
  `[a-zA-Z0-9_:]` become `_`, a recognised unit appends its long name (`s` → `_seconds`, `By` → `_bytes`,
  `1` → `_ratio` on gauges), a Sum appends `_total`, a Histogram is `<name>_bucket`/`_sum`/`_count` (the
  bare name is accepted as an alias of `_bucket`). Label names are sanitized attribute keys, plus
  `service_name` for the resource's `service.name`. A name is resolved against the metrics Flare has
  data for in the query window; an unknown name is an empty result (as in Prometheus), and a name
  mapping to two point types is an error.
- **Window semantics are bucket-based, and documented as such.** Samples are stamped with their bucket
  start (aligned to a multiple of the step), not `start + k*step`. A `[5m]` window slides over
  `round(range/step)` buckets (at least one); `rate` is the summed increase divided by the window's
  seconds and `increase` is that rate times the range. A bare gauge is the bucket average. An instant
  query evaluates the last five minutes at 60-second buckets and reports the last sample at the
  evaluation time.
- **Counters and untyped metrics.** Flare stores per-bucket increases, not running totals, so a bare
  counter selector is rejected with a message pointing at `rate()`/`increase()`. A Gauge under
  `rate()`/`increase()` is read as a counter (an untyped Prometheus `*_total` arrives as a Gauge), the
  same rule ADR-0066 gives the dashboard.
- **Metadata endpoints are bounded.** `series` requires `match[]`. `labels` and `label/<name>/values`
  without `match[]` sample the ten metrics with the most series and say so in `warnings`;
  `__name__` and `service_name` values come straight from the metric catalog.

## Alternatives considered

- **Embed or port a real PromQL engine.** Rejected: the Prometheus engine assumes a Prometheus storage
  interface and staleness model, and a port would carry its whole evaluation surface to maintain.
- **Generate dedicated ClickHouse SQL per PromQL construct.** Rejected: it duplicates the reset and
  temporality handling that ADR-0035/0060 took care to get right, and it is two implementations to keep
  in step.
- **Support vector/vector binary operators (error ratios).** Deferred, not rejected: it needs label
  matching (`on`/`ignoring`, one-to-one cardinality) and is where a "subset" starts to mislead. The
  parser's error says so. Ratios that matter have a first-class home as SLOs (ADR-0108).
- **A `/api/v1/write` or remote-read endpoint.** Out of scope: ingest stays OTLP-only (ADR-0012).

## Consequences

- `prometheus-adapter` and Grafana can query Flare for the supported shapes; anything else fails loudly
  at parse time.
- A selector matching more than 200 series is truncated with a warning. Narrow it with label matchers.
- Regex and `!=` matchers are applied after the fetch (equality matchers are pushed down), so on a
  metric with more than 200 series they can see a truncated set. The warning is the signal.
- Sample timestamps do not line up with `start + k*step`. Grafana renders this fine; a client that
  compares timestamps exactly will not.
- OTel non-monotonic Sums (UpDownCounters) also get `_total`, since the name mapper does not see
  `IsMonotonic`. `rate()` over one is not meaningful in Prometheus either.
- No `__name__` regex selection (`{__name__=~"http_.*"}`), no `offset`, no recording rules.
