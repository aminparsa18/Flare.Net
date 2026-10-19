# How to query Flare with Grafana or the Prometheus API

Flare serves a read-only **subset of the Prometheus HTTP API** over your OTel metrics, so Grafana,
`promtool` and `prometheus-adapter` (Kubernetes HPA on custom metrics) can use it as a Prometheus data
source. It covers selectors with `offset`, `rate`/`increase`, aggregations including `topk` and `quantile`,
`histogram_quantile`, operators between series (`a / b`) and a set of math functions.
Anything else is rejected with an error that names the unsupported construct, never answered partially.

## Connect Grafana

1. Create a [personal access token](../admin/configure-authentication.md#personal-access-tokens).
2. In Grafana, add a **Prometheus** data source.
3. Set **URL** to your Flare API base (`http://localhost:8080` in the standalone Docker stack). Flare serves the API at
   `/api/v1`, where Grafana expects it.
4. Under **Authentication**, add a custom HTTP header `Authorization` with the value
   `Bearer flr_pat_...`.
5. Click **Save & test**.

## Try it with curl

```bash
export FLARE=http://localhost:8080 TOKEN=flr_pat_...

# Instant query
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=sum by (service_name) (rate(http_server_requests_total[5m]))' \
  $FLARE/api/v1/query

# Range query
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" --data-urlencode step=60 \
  $FLARE/api/v1/query_range
```

## Metric and label names

Flare maps OTel names to Prometheus conventions:

| OTel | Prometheus |
|---|---|
| `http.server.request.duration` (histogram, unit `s`) | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |
| `http.server.requests` (sum) | `http_server_requests_total` |
| `process.memory` (gauge, unit `By`) | `process_memory_bytes` |
| attribute `http.route` | label `http_route` |
| resource `service.name` | label `service_name` |

Find the exact names with `GET /api/v1/label/__name__/values`.

## What is supported

| Construct | Example |
|---|---|
| Selectors, with `=`, `!=`, `=~`, `!~` | `up{service_name="api", code!="200"}` |
| Metric name patterns on `__name__` | `{__name__=~"http_server_.*"}`, `rate({__name__=~"jobs_.*_total"}[5m])` |
| `offset` on a selector or range | `requests_total offset 1w`, `rate(requests_total[5m] offset 1d)` |
| `rate`, `increase`, `irate` | `rate(requests_total[5m])` |
| `avg_over_time`, `min_over_time`, `max_over_time`, `sum_over_time`, `count_over_time`, `last_over_time` (gauges) | `max_over_time(queue_depth[10m])` |
| `delta`, `idelta`, `changes`, `resets`, `deriv`, `predict_linear`, `holt_winters` (gauges) | `predict_linear(free_bytes[6h], 4 * 3600)`, `resets(uptime_seconds[1d])` |
| `time()`, `timestamp(v)`, `scalar(v)`, and arithmetic on them; `vector(time())` | `time() - timestamp(heartbeat)` |
| `@` on a selector, range or subquery | `requests_total @ 1700000000`, `queue_depth / queue_depth @ start()` |
| Subqueries inside an `*_over_time` function, `delta`, `idelta`, `changes`, `resets`, `deriv`, `predict_linear` or `holt_winters` | `max_over_time(rate(requests_total[5m])[1h:1m])` |
| `sum`, `avg`, `min`, `max`, `count`, `group`, `stddev`, `stdvar` with `by`/`without` | `sum by (route) (rate(requests_total[5m]))` |
| `topk`, `bottomk`, `quantile` with `by`/`without` | `topk(5, sum by (host) (rate(cpu_seconds_total[5m])))` |
| `count_values` with `by`/`without` | `count_values("version", build_info)` |
| `sort`, `sort_desc`, `sort_by_label`, `sort_by_label_desc` | `sort_desc(sum by (host) (rate(cpu_seconds_total[5m])))` |
| `histogram_quantile` and `histogram_fraction` over `rate`/`increase`, optionally inside `sum by (...)` | `histogram_quantile(0.99, sum by (le, route) (rate(d_bucket[5m])))`, `histogram_fraction(0, 0.3, sum by (le) (rate(d_bucket[5m])))` |
| `_sum` and `_count` of a histogram under `rate`/`increase` | `rate(d_seconds_count[1m])` |
| Arithmetic `+ - * / % ^` between series and numbers | `100 * sum(rate(errors_total[5m])) / sum(rate(requests_total[5m]))` |
| Comparisons `== != > < >= <=`, filtering, or `bool` for 1/0 | `queue_depth > 100`, `up > bool 0` |
| `and`, `or`, `unless` | `rate(a_total[5m]) > 1 and on(job) rate(b_total[5m]) > 1` |
| `on(...)`, `ignoring(...)`, `group_left`, `group_right` | `errors / on(job) group_left(team) owners` |
| `abs`, `ceil`, `floor`, `round`, `sqrt`, `exp`, `ln`, `log2`, `log10`, `sgn`, `clamp`, `clamp_min`, `clamp_max`, `vector` | `clamp_min(free_ratio, 0)` |
| `absent` | `absent(up{job="api"})` |
| `label_replace`, `label_join` | `label_replace(x, "host", "$1", "instance", "(.*):\\d+")` |

Endpoints: `query`, `query_range`, `query_exemplars`, `series`, `labels`, `label/<name>/values`, `status/buildinfo`.

Not supported: `rate`/`increase`/`irate` over a subquery, the native-histogram functions (`histogram_count`, `histogram_sum`, `histogram_avg`; use a histogram's `_count` and `_sum` series), recording rules, and writing data.

## Release health

Flare serves the release health of mobile apps (see [Monitor a MAUI app](../../tutorials/monitor-a-maui-app.md)) as four built-in
metrics, so a Grafana panel or alert rule can use crash-free rates through the same data source. They are labelled
`service_name` and `service_version` and listed by `label/__name__/values`.

| Metric | Type | Meaning |
|---|---|---|
| `flare_release_sessions_total` | counter | Sessions that started |
| `flare_release_crashed_sessions_total` | counter | Sessions that ended in a fatal unhandled exception |
| `flare_release_users` | gauge | Distinct users with a session that started in the step |
| `flare_release_crashed_users` | gauge | Distinct users with a crashed session that started in the step |
| `flare_release_users_24h` | gauge | Distinct users with a session that started in the 24 hours up to the step |
| `flare_release_crashed_users_24h` | gauge | Distinct users with a crashed session that started in the 24 hours up to the step |

Crash-free sessions per version over the last 24 hours, for a panel or an alert rule below `0.99`:

```promql
1 - sum by (service_version) (increase(flare_release_crashed_sessions_total[24h]))
  / sum by (service_version) (increase(flare_release_sessions_total[24h]))
```

Add `{service_name="shop-app"}` to scope it to one app. To keep a version with three launches from paging anyone, add
`and sum by (service_version) (increase(flare_release_sessions_total[24h])) > 50`. Crash-free users over the last day
are `1 - flare_release_crashed_users_24h / flare_release_users_24h`: each step reports the 24 hours up to it, so an
instant query and a panel at any step both read a full day. The plain `flare_release_users` gauges count per step instead
(distinct users do not add up across steps), which suits a range query whose step is the period you want. The `_24h`
gauges scan a day of spans before the queried range, so a long range at a short step is the expensive case. Users appear
only when the app sets `user.id`. The `flare.release.` name prefix is reserved for these metrics.

## Annotations

Two more built-in metrics let Grafana mark alerts and deploys on any panel through an annotation query. Both are `1` where
the event is and absent elsewhere, and are listed by `label/__name__/values`.

| Metric | Labels | Meaning |
|---|---|---|
| `flare_alert_firing` | `alertname`, `rule_id` | `1` for every step an alert rule is firing, from its fire until it recovers |
| `flare_release_deployed` | `service_name`, `service_version`, `commit`, `url` | `1` at the step a [marked release](../investigate/track-releases.md) was deployed |

In a dashboard's **Settings > Annotations**, add an annotation query on the Flare data source:

| Field | Alerts | Deploys |
|---|---|---|
| Expression | `flare_alert_firing` | `flare_release_deployed{service_name="shop-app"}` |
| Min step | `1m` | `1m` |
| Title | `{{alertname}}` | `Deploy {{service_version}}` |
| Text | `Alert firing` | `{{commit}} {{url}}` |
| Tags | `alertname` | `service_name` |

An alert shows as a region from its fire to its recovery, one per incident (a re-fire during the cooldown is the same
incident); a rule that is still firing runs up to now. Fires that a maintenance window suppressed are not shown, because
nobody was notified. A rule's alerts follow its project: a project member sees only the rules of their own projects and the
instance-wide ones, and releases follow the service scope the other queries apply. Deploys come from the releases you
mark (`flare releases mark` or `PUT /api/releases`), not from a new version appearing in telemetry. The `flare.alert.`
name prefix and `flare.release.deployed` are reserved. A query fails when it would return more than 50,000 points; raise the
step or shorten the range.

## Exemplars

A histogram or counter point recorded inside a trace carries an **exemplar**: a sample value with the trace and
span that produced it. Flare keeps the ones that have a trace id and serves them from `query_exemplars`, so Grafana
can draw them on a latency chart and open the slow request.

1. In the Prometheus data source, enable **Exemplars** and add an internal link: **Label name** `trace_id`, **Data
   source** a Tempo data source pointing at Flare's [Tempo-compatible API](query-with-tempo-api.md).
2. Query a histogram, for example `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))`.
   The dots appear on the chart.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  --data-urlencode 'query=http_server_request_duration_seconds_bucket{service_name="api"}' \
  --data-urlencode "start=$(date -d '-1 hour' +%s)" --data-urlencode "end=$(date +%s)" \
  $FLARE/api/v1/query_exemplars
```

Each result is a series with its `exemplars`, each carrying `trace_id` (and `span_id`), a value and a timestamp. Up to
1000 are returned, newest first. Only points ingested after upgrading carry exemplars. They arrive over OTLP, or from a Prometheus scrape target with
`Exemplars` turned on (it asks the target for OpenMetrics and keeps the exemplars that carry a 32-hex-digit `trace_id`).

Flare's own Metrics page and dashboard Metrics panels draw them too: each exemplar is a ring that opens its trace on the span
that recorded it when clicked. On a histogram chart the ring sits at the observed value (one above the chart's range sits on
the top edge; hover for its true value). On a counter, gauge or formula chart it rides the line of the series it was
recorded on, at the bucket that contains it, since an increment has no height on a rate axis. The dashboard shows up to 100
of the newest in the window; stacked areas and comparison overlays draw none.

## Things that differ from Prometheus

- **Matching errors are real.** `a / b` fails with "found duplicate series for the match group" if a label set
  matches two series on one side. Aggregate first, narrow it with `on(...)`/`ignoring(...)`, or opt in with
  `group_left`/`group_right`, exactly as in Prometheus.
- **`offset` is rounded to whole steps.** `offset 90s` at a 60s step is read as 120s, so the shifted series stays on
  the same timestamps as the rest of the query.
- **`irate` and `avg_over_time` work on buckets.** `irate` is the newest bucket's increase per second, and
  `avg_over_time` averages bucket averages. `min_over_time`, `max_over_time`, `sum_over_time` and `count_over_time`
  reduce the raw points of each bucket, so they are exact down to the step.
- **`delta`, `changes` and `resets` compare each point with the one before it.** The comparison counts in the window that
  holds the later point, so a window includes the step in from the sample just before it. `delta(x[5m])` is the value at
  the window's end minus the value just before its start; Prometheus extrapolates last-minus-first instead, and counts
  only pairs that are both inside the window. They apply to gauges (a counter scraped without type metadata is one).
- **`deriv` and `predict_linear` fit the bucket averages.** The line goes through each bucket's average at the middle of
  the bucket, so use a window of several steps. `predict_linear` reads the line at the end of the newest bucket.
- **`idelta` is the newest point minus the one before it**, even when that one is older than the window. **`holt_winters`** (also `double_exponential_smoothing`) smooths the bucket averages, so it needs two buckets.
- **`timestamp(x)` is the time of the newest point** behind each sample, which makes `time() - timestamp(x)` the real age of a metric.
- **A `__name__` pattern** is evaluated once for each matching metric, up to 50 (alphabetical), and the series are put together. A metric the expression cannot read, such as a counter outside `rate()`, is skipped with a warning. `histogram_quantile` and `histogram_fraction` need one name.
- **`time()` is the step's timestamp**, or the exact query time in an instant query. **`sort` and `sort_desc`** only
  show in an instant query, as in Prometheus.
- **`@` pins the evaluation time.** `x @ 1700000000` is evaluated once, like an instant query at that time (newest sample within the five-minute lookback), and repeated at
  every step. In an instant query `start()` and `end()` both mean the query time.
- **Subqueries** `expr[1h:1m]` evaluate the inner expression every `1m` (the query step when the resolution is
  left out) and reduce the samples in `(t - 1h, t]`. They need an `*_over_time` function around them.
- **Counters need `rate()` or `increase()`.** Flare stores per-interval increases, so a bare counter
  such as `requests_total` is rejected with a pointer to `rate()`. A gauge used under `rate()` is read as
  a counter, which is how untyped Prometheus `*_total` metrics arrive.
- **Timestamps are bucket starts.** Samples sit on multiples of the step, not on `start + k*step`.
  A `[5m]` window slides over `round(5m / step)` buckets, at least one.
- **200 series per selector.** A selector matching more series returns the 200 largest and adds a
  `warnings` entry. Add label matchers to narrow it. `!=` and regex matchers are applied after that
  cap.
- **Instant queries** look at the last five minutes.
- **`labels` and label values without `match[]`** sample the ten metrics with the most series. Pass
  `match[]` for a complete answer.

Design notes: [ADR-0109](../../../docs-internal/adr/0109-prometheus-query-api.md), [ADR-0239](../../../docs-internal/adr/0239-prometheus-api-binary-operators-offset-topk-functions.md), [ADR-0245](../../../docs-internal/adr/0245-prometheus-api-at-modifier-subqueries-min-max-over-time.md), [ADR-0246](../../../docs-internal/adr/0246-prometheus-api-time-scalar-delta-changes-resets-deriv-sort-count-values.md), [ADR-0247](../../../docs-internal/adr/0247-prometheus-api-idelta-holt-winters-timestamp-histogram-fraction-name-patterns.md), [ADR-0241](../../../docs-internal/adr/0241-metric-exemplars.md), [ADR-0250](../../../docs-internal/adr/0250-release-health-prometheus-metrics.md).
